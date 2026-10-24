using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AisoundStudio;

public partial class MainWindow : Window
{
    readonly string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    string root = "";
    string exportDir = "";
    string DataDir => Path.Combine(appDir, "data");
    string ExportDir => exportDir;
    string Python => Path.Combine(root, "runtime", "Scripts", "python.exe");
    readonly Dictionary<string, StackPanel> pages = new();
    readonly Dictionary<string, Button> nav = new();
    readonly List<string> trainingInputs = new();
    readonly List<JobRecord> history = new();
    readonly List<VoiceRecord> voiceRecords = new();
    readonly List<Button> actionButtons = new();
    readonly ScrollViewer content = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock breadcrumb = Txt("创作工作台 / 人声分离", 12, "#A5AFBC");
    readonly TextBlock engineLabel = Txt("●  检查运行环境", 11, "#E9C88C");
    readonly TextBlock taskTitle = Txt("工作台已就绪", 13, "#F0F4F8", true);
    readonly TextBlock taskPercent = Txt("空闲", 12, "#ADF0D1");
    readonly TextBox taskLog = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 11, FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brush("#9AA8B8"), Padding = new Thickness(0, 7, 0, 0), MaxHeight=double.PositiveInfinity, MinHeight=0 };
    readonly ProgressBar progress = new() { Height = 3, Maximum = 100, Foreground = Brush("#ADF0D1"), Background = Brush("#303B43"), BorderThickness = new Thickness(0) };
    readonly RowDefinition taskRow = new() { Height=new GridLength(104) };
    Button cancelButton = null!;
    TextBlock splitName = null!, splitDetail = null!, trainingCount = null!;
    TextBox splitOutput = null!, coverInput = null!, coverOutput = null!, voiceName = null!;
    TextBox videoInput = null!, videoOutput = null!;
    TextBlock videoName = null!, videoDetail = null!;
    ComboBox bitrate = null!, videoBitrate = null!, epochs = null!, batch = null!, voiceChoice = null!;
    CheckBox splitWav = null!, splitReverb = null!, coverWav = null!, coverReverb = null!, vocalOnly = null!, videoWav = null!;
    ListBox trainFiles = null!, voiceList = null!, historyList = null!, outputList = null!;
    Slider pitch = null!, indexRate = null!, vocalGain = null!, instGain = null!;
    StackPanel checksPanel = null!;
    Border libraryEmpty = null!;
    TextBox engineDirectoryBox = null!;
    readonly TextBlock playerStatus = Txt("选择 MP3 或 WAV 音频开始试听", 12, "#9AA8B8");
    readonly MediaElement player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Visibility = Visibility.Collapsed };
    bool busy, cancelRequested;
    Process? running;
    ProcessJob? processJob;
    JobRecord? activeJob;
    JsonElement diagnostic;
    string splitInput = "";
    string currentPage = "split";
    string resultFolder = "";
    string[] resultOutputs = [];
    string currentAudio = "";
    readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow()
    {
        Title = "声屿 · Aisound Studio";
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico"));
        Width = Math.Min(1240,SystemParameters.WorkArea.Width-40);
        Height = Math.Min(950,SystemParameters.WorkArea.Height-45);
        MinWidth = 1040; MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#11151A"); Foreground = Brush("#F0F4F8");
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        System.Windows.Media.TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        System.Windows.Media.TextOptions.SetTextRenderingMode(this,TextRenderingMode.Grayscale);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
        LoadSettings();
        Directory.CreateDirectory(DataDir); Directory.CreateDirectory(ExportDir);
        Directory.CreateDirectory(Path.Combine(appDir, "datasets"));
        Content = BuildShell();
        BuildPages();
        ReadHistory(); LoadVoices(); SwitchPage("split");
        player.MediaEnded += (_, _) => { player.Stop(); playerStatus.Text = "试听结束"; };
        player.MediaFailed += (_, e) => playerStatus.Text = "无法播放：" + e.ErrorException.Message;
        Loaded += async (_, _) =>
        {
            try { int enabled = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref enabled, 4); } catch { }
            await ProbeAsync();
            await HandleValidationArgumentsAsync();
        };
        Closing += (_, e) =>
        {
            if (busy)
            {
                if (MessageBox.Show("关闭软件会取消当前任务，已经保存的文件仍会保留。是否关闭？", "任务正在运行", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { e.Cancel = true; return; }
                CancelActive();
            }
            player.Close();
        };
    }

    static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    static TextBlock Txt(string text, double size = 13, string color = "#F0F4F8", bool bold = false) => new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    static StackPanel Stack(params UIElement[] children) { var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    static Border Card(UIElement child, double padding = 23) => new() { Background = Brush("#1C232B"), BorderBrush = Brush("#303A44"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(padding), Child = child, Margin = new Thickness(0, 0, 0, 17) };
    static TextBlock Hint(string text) { var t = Txt(text, 12, "#94A3B3"); t.LineHeight = 21; return t; }
    Button Btn(string text, Action click, bool secondary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(17, 11, 17, 11) };
        if (secondary) { b.Background = Brush("#2C3640"); b.Foreground = Brush("#E2EAF2"); }
        b.Click += (_, _) => { try { click(); } catch (Exception e) { Notify(e.Message); } };
        return b;
    }
    Button ActionBtn(string text, Func<Task> action)
    {
        var b = Btn(text, async () => { if (busy) return; try { await action(); } catch (Exception e) { Notify(e.Message); } });
        actionButtons.Add(b); return b;
    }
    static FrameworkElement Row(UIElement left, UIElement right, double gap = 18)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(left); Grid.SetColumn(right, 2); g.Children.Add(right); return g;
    }
    static FrameworkElement Field(string label, UIElement control)
    {
        var caption = Hint(label); caption.Margin = new Thickness(0, 0, 0, 7); return Stack(caption, control);
    }
    static FrameworkElement Spread(UIElement left, UIElement right)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(left); Grid.SetColumn(right, 1); g.Children.Add(right); return g;
    }
    static StackPanel Horizontal(params UIElement[] items)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items) { if (item is FrameworkElement f) f.Margin = new Thickness(0, 0, 10, 0); s.Children.Add(item); } return s;
    }
    static CheckBox Check(string text, bool value = false) => new() { Content = text, IsChecked = value };
    static ComboBox Combo(string[] choices, int selected = 0) { var c = new ComboBox(); foreach (var x in choices) c.Items.Add(x); c.SelectedIndex = selected; return c; }

    UIElement BuildShell()
    {
        var shell = new Grid { Background=Brush("#11151A") };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        var side = new Grid { Margin = new Thickness(18, 27, 18, 22) };
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(108) }); side.RowDefinitions.Add(new RowDefinition()); side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var logo = new Border { Background = Brush("#ADF0D1"), CornerRadius = new CornerRadius(12), Width = 39, Height = 39, Child = Txt("∿", 30, "#244235", true) };
        ((TextBlock)logo.Child).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)logo.Child).VerticalAlignment = VerticalAlignment.Center;
        var title = Txt("声屿", 26, "#F0F4F8", true); title.Margin = new Thickness(4,0,0,0);
        var name = Stack(Horizontal(logo, title), Txt("AISOUND STUDIO", 10, "#8C9BAD")); name.Margin = new Thickness(10,0,0,0); side.Children.Add(name);
        var navigation = Stack(Hint("创作工作台")); navigation.Children[0].SetValue(MarginProperty, new Thickness(14,0,0,13));
        foreach (var (key, label) in new[] { ("split","01   人声分离"), ("train","02   训练音色"), ("cover","03   制作翻唱"), ("video","04   视频转音频"), ("library","♫    音色库"), ("history","◷    任务与导出"), ("settings","⚙    环境与设置") })
        {
            if (key == "library") { navigation.Children.Add(new Border { Height = 1, Background = Brush("#303B45"), Margin = new Thickness(12,22,12,20) }); navigation.Children.Add(Hint("你的工作空间")); }
            var button = Btn(label, () => SwitchPage(key), true);
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(16,14,16,14); button.Margin = new Thickness(0,4,0,4);
            // Template content is wrapped to preserve left alignment.
            button.Content = new TextBlock { Text = label, Width = 142, TextAlignment = TextAlignment.Left };
            nav[key] = button; navigation.Children.Add(button);
        }
        Grid.SetRow(navigation,1); side.Children.Add(navigation);
        var local = Card(Stack(Txt("在你的电脑上创作",12,"#DFE8EF",true),Hint("音频和音色保存在本地"),Txt("v1.0 · Windows",10,"#728396")),15); local.Margin = new Thickness(0);
        Grid.SetRow(local,2); side.Children.Add(local);
        shell.Children.Add(new Border { Background = Brush("#171D24"), BorderBrush = Brush("#2A343D"), BorderThickness = new Thickness(0,0,1,0), Child = side });
        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(taskRow);
        var badge = new Border { Background = Brush("#352D23"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12,7,12,7), Child = engineLabel, VerticalAlignment = VerticalAlignment.Center };
        var open = Btn("打开导出目录 ↗", () => OpenFolder(ExportDir),true); open.Padding = new Thickness(12,8,12,8);
        var top = Spread(breadcrumb, Horizontal(badge,open)); top.VerticalAlignment = VerticalAlignment.Center;
        main.Children.Add(new Border { Padding = new Thickness(30,0,20,0), BorderBrush = Brush("#2A343D"), BorderThickness = new Thickness(0,0,0,1), Child = top });
        Grid.SetRow(content,1); main.Children.Add(content);
        cancelButton = Btn("取消任务",CancelActive,true); cancelButton.Padding = new Thickness(12,5,12,5); cancelButton.FontSize = 11; cancelButton.IsEnabled = false;
        taskPercent.VerticalAlignment = VerticalAlignment.Center;
        var bottom = new Grid(); bottom.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) }); bottom.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) }); bottom.RowDefinitions.Add(new RowDefinition());
        bottom.Children.Add(Spread(taskTitle,Horizontal(taskPercent,cancelButton))); Grid.SetRow(progress,1); bottom.Children.Add(progress); Grid.SetRow(taskLog,2); bottom.Children.Add(taskLog);
        var bottomFrame = new Border { Background = Brush("#181F27"), BorderBrush = Brush("#303A45"), BorderThickness = new Thickness(0,1,0,0), Padding = new Thickness(30,15,20,12), Child = bottom };
        Grid.SetRow(bottomFrame,2); main.Children.Add(bottomFrame);
        Grid.SetColumn(main,1); shell.Children.Add(main); return shell;
    }

    StackPanel Page(string key, string eyebrow, string title, string subtitle)
    {
        var p = new StackPanel { Margin = new Thickness(30,25,30,24) };
        p.Children.Add(Txt(eyebrow,11,"#ADF0D1",true));
        var h = Txt(title,29,"#F0F4F8",true); h.Margin = new Thickness(0,9,0,9); p.Children.Add(h);
        var hint = Hint(subtitle); hint.Margin = new Thickness(0,0,0,23); p.Children.Add(hint);
        pages[key] = p; return p;
    }
    FrameworkElement OutputField(TextBox box)
    {
        SetExportDisplay(box);
        var button=Btn("选择目录",()=>PickFolder(box),true);
        button.Margin=new Thickness(8,0,0,0);
        return Spread(box,button);
    }
    void BuildPages()
    {
        BuildSplit(); BuildTraining(); BuildCover(); BuildVideo(); BuildLibrary(); BuildHistory(); BuildSettings();
    }
    void BuildSplit()
    {
        var p = Page("split","SEPARATE / 01","留下人声，打开创作的第一步。","导入一首歌曲，分别获得人声和伴奏。两条音轨都可导出为 MP3。");
        splitName = Txt("拖入歌曲，或点击选择文件",16,"#E7EFF5",true); splitName.TextTrimming = TextTrimming.CharacterEllipsis; splitName.TextWrapping = TextWrapping.NoWrap;
        splitDetail = Hint("MP3 / WAV / FLAC / M4A / AAC / OGG");
        var up = Txt("↥",34,"#ADF0D1"); var dropContent = Stack(up,splitName,splitDetail);
        foreach (FrameworkElement child in dropContent.Children) { child.HorizontalAlignment = HorizontalAlignment.Center; child.Margin = new Thickness(0,4,0,4); }
        var drop = new Border { Background = Brush("#141D22"), BorderBrush = Brush("#3B5148"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(23), MinHeight = 153, Cursor = Cursors.Hand, AllowDrop = true, Child = dropContent };
        drop.PreviewDragOver += AcceptFileDrop;
        drop.MouseLeftButtonUp += (_,_) => PickSplit(); drop.Drop += (_,e) => { var f = Dropped(e); if (f.Length>0) SetSplit(f[0]); };
        var two = Stack(Txt("一次分离，两份音轨",14,"#E5EEF5",true), Hint("人声 · 用于音色训练或转换"),Hint("伴奏 · 用于完整翻唱"));
        foreach (FrameworkElement child in two.Children) child.Margin = new Thickness(0,8,0,4);
        var quality = new Border { Background = Brush("#293D32"), CornerRadius = new CornerRadius(7), Padding = new Thickness(10,7,10,7), Child = Txt("MP3 · 最高 320 kbps",11,"#ADF0D1"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,12,0,0) }; two.Children.Add(quality);
        var upload = new Grid(); upload.ColumnDefinitions.Add(new ColumnDefinition()); upload.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) }); upload.Children.Add(drop); two.Margin = new Thickness(24,0,0,0); Grid.SetColumn(two,1); upload.Children.Add(two); p.Children.Add(Card(upload,20));
        splitOutput = new TextBox { IsReadOnly = true };
        bitrate = Combo(["320 kbps · 高品质","192 kbps · 标准","128 kbps · 小文件"]);
        var fields = Row(Field("导出位置", OutputField(splitOutput)),Field("MP3 音质",bitrate)); fields.Margin = new Thickness(0,18,0,10);
        splitWav = Check("同时保留 WAV 音轨，方便后续训练",true); splitReverb = Check("额外去除人声混响（需要去混响模型）");
        var controls = Spread(Stack(splitWav,splitReverb),ActionBtn("开始分离   →",StartSplitAsync)); controls.Margin = new Thickness(0,8,0,0); controls.VerticalAlignment = VerticalAlignment.Bottom;
        p.Children.Add(Card(Stack(Txt("导出设置",16,"#F0F4F8",true),fields,controls)));
        p.Children.Add(Hint("下一步 · 将导出的人声送去训练，或使用已有音色制作翻唱。"));
    }
    void BuildTraining()
    {
        var p = Page("train","VOICE TRAINING / 02","把熟悉的声音，存进音色库。","导入同一个人的干净独唱。自动切分、提取音高、训练音色并生成检索索引。");
        trainFiles = new ListBox { Height = 122, Margin = new Thickness(0,15,0,9), AllowDrop = true };
        trainFiles.Drop += (_,e) => AddTrainingFiles(Dropped(e));
        trainFiles.PreviewDragOver += AcceptFileDrop;
        trainingCount = Hint("尚未添加素材 · 可拖入多个音频文件");
        var buttons = Horizontal(Btn("移除所选",RemoveTraining,true),Btn("＋ 添加人声",PickTraining));
        var tip = new Border { Background = Brush("#28392E"), CornerRadius = new CornerRadius(8), Padding = new Thickness(13,11,13,11), Margin = new Thickness(0,13,0,0), Child = Txt("素材建议：至少约 10 分钟清晰独唱，覆盖不同音高。优先使用 WAV，避免和声、其他歌手与伴奏残留。",12,"#BDD0C1") };
        p.Children.Add(Card(Stack(Spread(Txt("训练素材",16,"#F0F4F8",true),buttons),trainFiles,trainingCount,tip)));
        voiceName = new TextBox { Text = "我的音色", MaxLength = 40 };
        epochs = Combo(["100 · 快速试做","200 · 推荐","300 · 更长训练"],1); batch = Combo(["1 · 节省显存","2 · 推荐 8GB"],1);
        var options = Row(Field("音色名称",voiceName),Row(Field("训练轮数",epochs),Field("批次大小",batch))); options.Margin = new Thickness(0,18,0,18);
        var spec = Stack(Txt("RVC v2 / 40 kHz / RMVPE",12,"#ADF0D1"),Hint("自动保存到引擎可读取的位置，并加入音色库。"));
        p.Children.Add(Card(Stack(Txt("音色与训练参数",16,"#F0F4F8",true),options,Spread(spec,ActionBtn("训练并保存音色  →",StartTrainingAsync)))));
    }
    void BuildCover()
    {
        var p = Page("cover","VOICE CONVERSION / 03","为喜欢的歌曲，换一种声音。","选择歌曲和音色，自动分离、转换人声、混合伴奏，导出完整翻唱。");
        coverInput = new TextBox { IsReadOnly = true, AllowDrop = true }; coverInput.Drop += (_,e) => { var f = Dropped(e); if (f.Length>0) SetCover(f[0]); };
        coverInput.PreviewDragOver += AcceptFileDrop;
        voiceChoice = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id" };
        vocalOnly = Check("导入的是纯人声，只转换音色（无需分离）");
        var sourceFields = Row(Field("原歌曲",Spread(coverInput,Btn("导入",PickCover,true))),Field("演唱音色",voiceChoice)); sourceFields.Margin = new Thickness(0,0,0,12);
        p.Children.Add(Card(Stack(sourceFields,Spread(vocalOnly,ActionBtn("导入音色文件 ↗",ImportVoiceAsync)))));
        pitch = MakeSlider(-12,12,0,true); indexRate = MakeSlider(0,1,.65); vocalGain = MakeSlider(0,2,1); instGain = MakeSlider(0,2,1);
        var tuning = Row(Stack(SliderField("变调 / 半音",pitch,x=>((int)x).ToString()),SliderField("人声音量",vocalGain,x=>$"{x*100:0}%")),Stack(SliderField("音色相似度 / 索引混合",indexRate,x=>$"{x*100:0}%"),SliderField("伴奏音量",instGain,x=>$"{x*100:0}%")),30); tuning.Margin = new Thickness(0,17,0,5);
        p.Children.Add(Card(Stack(Txt("声音调节",16,"#F0F4F8",true),tuning,Hint("从原调 0 开始，跨音域可尝试 ±12。未提供索引时，相似度调节不参与计算。"))));
        coverOutput = new TextBox { IsReadOnly = true }; coverWav = Check("导出 320 kbps MP3，同时保留 WAV",true); coverReverb = Check("转换前额外去混响");
        p.Children.Add(Card(Stack(OutputField(coverOutput),Spread(Stack(coverWav,coverReverb),ActionBtn("生成翻唱   →",StartCoverAsync))),20));
    }
    void BuildVideo()
    {
        var p = Page("video","VIDEO TO AUDIO / 04","把视频里的声音取出来。","导入视频文件，提取音轨并导出为 MP3。无需 AI 引擎，也不影响原视频文件。");
        videoName = Txt("拖入视频，或点击选择文件",16,"#E7EFF5",true); videoName.TextTrimming = TextTrimming.CharacterEllipsis; videoName.TextWrapping = TextWrapping.NoWrap;
        videoDetail = Hint("MP4 / MOV / MKV / WEBM / AVI / FLV / M4V / TS");
        var up = Txt("⬒",34,"#ADF0D1"); var dropContent = Stack(up,videoName,videoDetail);
        foreach (FrameworkElement child in dropContent.Children) { child.HorizontalAlignment = HorizontalAlignment.Center; child.Margin = new Thickness(0,4,0,4); }
        var drop = new Border { Background = Brush("#141D22"), BorderBrush = Brush("#3B5148"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(23), MinHeight = 153, Cursor = Cursors.Hand, AllowDrop = true, Child = dropContent };
        drop.PreviewDragOver += AcceptFileDrop;
        drop.MouseLeftButtonUp += (_,_) => PickVideo(); drop.Drop += (_,e) => { var f = Dropped(e); if (f.Length>0) SetVideo(f[0]); };
        var notes = Stack(Txt("只提取声音，画面自动去除",14,"#E5EEF5",true),Hint("音轨会被编码成你选择的 MP3 音质"),Hint("原始视频保持不变，随时可重新导出"),Hint("视频里的 BGM 和人声会一起保留。需要拆开请前往人声分离。"));
        foreach (FrameworkElement child in notes.Children) child.Margin = new Thickness(0,8,0,4);
        var badge = new Border { Background = Brush("#293D32"), CornerRadius = new CornerRadius(7), Padding = new Thickness(10,7,10,7), Child = Txt("纯 FFmpeg · 无需 AI 引擎",11,"#ADF0D1"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,12,0,0) }; notes.Children.Add(badge);
        var upload = new Grid(); upload.ColumnDefinitions.Add(new ColumnDefinition()); upload.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) }); upload.Children.Add(drop); notes.Margin = new Thickness(24,0,0,0); Grid.SetColumn(notes,1); upload.Children.Add(notes); p.Children.Add(Card(upload,20));
        videoOutput = new TextBox { IsReadOnly = true };
        videoBitrate = Combo(["320 kbps · 高品质","192 kbps · 标准","128 kbps · 小文件"]);
        videoWav = Check("同时保留 WAV，方便后续分离或训练",true);
        var fields = Row(Field("导出位置", OutputField(videoOutput)),Field("MP3 音质",videoBitrate)); fields.Margin = new Thickness(0,18,0,10);
        p.Children.Add(Card(Stack(Txt("导出设置",16,"#F0F4F8",true),fields,Spread(videoWav,ActionBtn("提取音频   →",StartVideoAsync)))));
        p.Children.Add(Hint("下一步 · 导出的 MP3 可以直接用于人声分离、训练音色或制作翻唱。"));
    }
    static Slider MakeSlider(double min,double max,double value,bool snap=false) => new() { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = snap, Margin = new Thickness(0,10,0,13) };
    static FrameworkElement SliderField(string caption,Slider slider,Func<double,string> format)
    {
        var label = Txt(format(slider.Value),12,"#ADF0D1"); slider.ValueChanged += (_,e) => label.Text = format(e.NewValue);
        return Stack(Spread(Hint(caption),label),slider);
    }
    void BuildLibrary()
    {
        var p = Page("library","YOUR VOICES","你的音色，随时登场。","训练与导入的音色集中保存在本地。选中后可直接用于翻唱，也可导出模型和索引。");
        var add = ActionBtn("＋ 导入音色模型",ImportVoiceAsync);
        var search = ActionBtn("从网页搜索并下载音色",SearchVoiceCatalogAsync);
        var actions = Horizontal(add,search); actions.Margin = new Thickness(0,0,0,18); p.Children.Add(actions);
        libraryEmpty = Card(Stack(Txt("音色库还是空的",20,"#EAF2F8",true),Hint("训练一份属于你的音色，或导入已有的 RVC .pth 模型。"),Btn("去训练第一个音色 →",()=>SwitchPage("train"))),30); p.Children.Add(libraryEmpty);
        voiceList = new ListBox { MinHeight = 80, MaxHeight = 320, DisplayMemberPath = "Summary", Margin = new Thickness(0,0,0,18) }; p.Children.Add(voiceList);
        p.Children.Add(Horizontal(Btn("用这个音色翻唱",UseVoice),Btn("重命名",RenameVoice,true),Btn("导出音色文件",ExportVoice,true),Btn("刷新音色库",LoadVoices,true)));
    }
    void BuildHistory()
    {
        var p = Page("history","WORKSPACE","每一次创作，都有记录。","任务状态、日志和导出的音频都保存在这里，已有文件不会被下一次任务覆盖。");
        historyList = new ListBox { Height = 210, DisplayMemberPath = "Summary", Margin = new Thickness(0,0,0,17) };
        historyList.SelectionChanged += (_,_) => SelectHistory(); p.Children.Add(historyList);
        outputList = new ListBox { Height = 100, Margin = new Thickness(0,14,0,14), DisplayMemberPath = "Name" };
        outputList.SelectionChanged += (_,_) => { if (outputList.SelectedItem is FileItem f) { currentAudio = f.Path; playerStatus.Text = f.Name; } };
        playerStatus.Margin = new Thickness(0,14,0,0);
        p.Children.Add(Card(Stack(Txt("导出文件与试听",16,"#F0F4F8",true),outputList,Horizontal(Btn("▶  试听",Play),Btn("停止",()=> { player.Stop(); playerStatus.Text="试听已停止"; },true),Btn("另存到…",SaveOutput,true),Btn("打开文件夹 ↗",()=>OpenFolder(resultFolder),true)),playerStatus,player)));
        p.Children.Add(Horizontal(Btn("将所选人声送去训练 →",SendToTraining,true),Btn("查看任务日志",OpenJobLog,true)));
    }
    void BuildSettings()
    {
        var p = Page("settings","ENGINE & SETTINGS","准备好你的本地创作引擎。","桌面界面可独立启动；AI 功能需要显卡依赖和模型。下载支持续传，安装进度显示在下方。");
        checksPanel = new StackPanel { Margin = new Thickness(0,15,0,13) };
        var refresh = Btn("重新检测",async()=>await ProbeAsync(),true);
        engineDirectoryBox = new TextBox { IsReadOnly = true, ToolTip = root };
        var engineLocation = Spread(engineDirectoryBox,Btn("选择目录",ChooseEngineDirectory,true));
        UpdateEngineDirectoryDisplay();
        p.Children.Add(Card(Stack(Spread(Txt("引擎检查",16,"#F0F4F8",true),refresh),checksPanel,Field("AI 环境下载位置",engineLocation),Horizontal(ActionBtn("安装 / 继续下载 AI 引擎",InstallEngineAsync),ActionBtn("只补齐 MP3 工具",InstallAudioAsync)),Hint("首次安装需要数 GB 网络流量，只会在你确认后开始。下载可取消，已完成文件会保留以便续传。"))));
        p.Children.Add(Card(Stack(Txt("下载来源",16,"#F0F4F8",true),Hint("AI 引擎源码：github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI"),Hint("Python 环境：python.org · PyTorch：download.pytorch.org"),Hint("AI 模型：huggingface.co/lj1995/VoiceConversionWebUI"),Hint("在线音色目录：voice-models.com（社区内容；模型授权由上传者负责）"))));
        var audio = Stack(Txt("音频工具",16,"#F0F4F8",true),Hint("无需 AI 模型，即可将已有音频转换为 320 kbps MP3。"),ActionBtn("选择音频并导出 MP3 →",TranscodeAsync));
        foreach (FrameworkElement f in audio.Children) f.Margin = new Thickness(0,0,0,12); p.Children.Add(Card(audio));
    }
    void SwitchPage(string key)
    {
        if (!pages.TryGetValue(key,out var page)) return;
        currentPage = key; content.Content = page; content.ScrollToTop();
        foreach (var (name,b) in nav) { b.Background = Brush(name==key?"#2B4539":"#171D24"); b.Foreground = Brush(name==key?"#ADF0D1":"#BCC9D5"); }
        breadcrumb.Text = "声屿工作台 / " + new Dictionary<string,string> { ["split"]="人声分离",["train"]="训练音色",["cover"]="制作翻唱",["video"]="视频转音频",["library"]="音色库",["history"]="任务与导出",["settings"]="环境与设置" }[key];
    }
}

public class VoiceRecord
{
    public override string ToString() => Name;
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("index")] public string Index { get; set; } = "";
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sample_rate")] public string SampleRate { get; set; } = "";
    [JsonIgnore] public string Summary => $"{Name}   ·   {Version} / {SampleRate}   ·   {Source}\n{Created}   ·   {(string.IsNullOrEmpty(Index)?"无索引":"模型 + 索引")}";
}
public class JobRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Status { get; set; } = "运行中";
    public string Created { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    public string Folder { get; set; } = "";
    public string[] Outputs { get; set; } = [];
    public string Log { get; set; } = "";
    public string Error { get; set; } = "";
    [JsonIgnore] public string Summary => $"{Title}   ·   {Status}\n{Created}" + (Error.Length>0?"   ·   "+Error.Split('\n')[0]:"");
}
public record FileItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public override string ToString() => Name;
}
