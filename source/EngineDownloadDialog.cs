using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace AisoundStudio;

public sealed class EngineDownloadDialog : Window
{
    readonly TextBox location;
    public string EngineRoot => Path.GetFullPath(location.Text.Trim());

    public EngineDownloadDialog(string currentRoot, string appDirectory, bool audioOnly = false)
    {
        Title = audioOnly ? "下载音频工具" : "下载 AI 环境";
        Width = 700;
        Height = 550;
        MinWidth = 640;
        MinHeight = 500;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Color("#192028");
        Foreground = Color("#F0F4F8");
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });

        var defaultRoot = Path.Combine(appDirectory, "AIEnvironment");
        location = new TextBox { Text = string.IsNullOrWhiteSpace(currentRoot) ? defaultRoot : currentRoot, IsReadOnly = true };
        var browse = new Button { Content = "选择目录", Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => ChooseLocation();

        var sources = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        sources.Children.Add(SourceLink("Python 运行环境", "https://www.python.org/downloads/release/python-31210/"));
        sources.Children.Add(SourceLink("FFmpeg Python 包", "https://pypi.org/project/imageio-ffmpeg/"));
        if (!audioOnly)
        {
            sources.Children.Add(SourceLink("RVC 引擎源码", "https://github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI"));
            sources.Children.Add(SourceLink("PyTorch GPU 组件", "https://download.pytorch.org/whl/"));
            sources.Children.Add(SourceLink("AI 模型文件", "https://huggingface.co/lj1995/VoiceConversionWebUI"));
        }

        var cancel = new Button { Content = "取消", Padding = new Thickness(17, 10, 17, 10), IsCancel = true };
        var download = new Button { Content = "确认并下载", Padding = new Thickness(17, 10, 17, 10), IsDefault = true, Margin = new Thickness(10, 0, 0, 0) };
        download.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(location.Text))
            {
                MessageBox.Show(this, "请选择 AI 环境保存位置。", "声屿", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { _ = Path.GetFullPath(location.Text.Trim()); }
            catch { MessageBox.Show(this, "下载位置无效，请重新选择。", "声屿", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            DialogResult = true;
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = audioOnly ? "尚未检测到音频处理工具" : "尚未检测到完整的 AI 环境", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(Text(audioOnly
            ? "下载 Python 和 FFmpeg 音频工具，用于 MP3 导出与视频转音频；不会下载 RVC、PyTorch 或 AI 模型。只会在你点击“确认并下载”后开始。"
            : "下载会使用数 GB 网络流量，并在选定目录创建 Python 环境、RVC 引擎和 AI 模型。只会在你点击“确认并下载”后开始。", "#A5B3C0"));
        panel.Children.Add(Text("默认保存到软件目录下新建的 AIEnvironment 文件夹。选择其他目录不会移动或删除已有文件。", "#A5B3C0"));
        var locationLabel = new TextBlock { Text = "下载位置", Foreground = Color("#A5B3C0"), Margin = new Thickness(0, 19, 0, 7) };
        panel.Children.Add(locationLabel);
        var locationRow = new Grid();
        locationRow.ColumnDefinitions.Add(new ColumnDefinition());
        locationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        locationRow.Children.Add(location);
        Grid.SetColumn(browse, 1); locationRow.Children.Add(browse);
        panel.Children.Add(locationRow);
        panel.Children.Add(new TextBlock { Text = "下载来源（点击可核对）", Foreground = Color("#A5B3C0"), Margin = new Thickness(0, 19, 0, 2) });
        panel.Children.Add(sources);
        panel.Children.Add(Text(audioOnly
            ? "音频工具只从 Python.org 和 PyPI 下载，不需要 NVIDIA 显卡。此功能不要求管理员权限。"
            : "RVC 社区音色和 AI 模型可能有各自的授权条件；下载前请查看来源页面。此功能不要求管理员权限。", "#E9C88C"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(cancel); buttons.Children.Add(download); panel.Children.Add(buttons);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }

    void ChooseLocation()
    {
        var initial = Directory.Exists(location.Text) ? location.Text : Path.GetDirectoryName(location.Text);
        var dialog = new OpenFolderDialog { Title = "选择或新建 AI 环境目录", InitialDirectory = initial };
        if (dialog.ShowDialog(this) == true) location.Text = Path.GetFullPath(dialog.FolderName);
    }

    static TextBlock Text(string value, string color) => new() { Text = value, Foreground = Color(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), LineHeight = 21 };
    static System.Windows.Media.SolidColorBrush Color(string hex) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
    static TextBlock SourceLink(string label, string url)
    {
        var block = new TextBlock { Margin = new Thickness(0, 3, 0, 3) };
        var link = new Hyperlink(new Run(label + " · " + new Uri(url).Host)) { NavigateUri = new Uri(url) };
        link.RequestNavigate += (_, e) => Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        block.Inlines.Add(link);
        return block;
    }
}
