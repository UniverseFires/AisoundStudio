using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AisoundStudio;
public partial class MainWindow
{
    const string AudioFilter = "音频文件|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma|所有文件|*.*";
    const string VideoFilter = "视频文件|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.flv;*.wmv;*.m4v;*.ts;*.mpg;*.mpeg|所有文件|*.*";
    readonly SemaphoreSlim probeLock = new(1,1);
    static bool IsAudio(string path) => File.Exists(path) && new[] { ".mp3",".wav",".flac",".m4a",".aac",".ogg",".wma" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    static bool IsVideo(string path) => File.Exists(path) && new[] { ".mp4",".mov",".mkv",".webm",".avi",".flv",".wmv",".m4v",".ts",".mpg",".mpeg" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    void Notify(string message) => MessageBox.Show(this,message,"声屿 · 提示",MessageBoxButton.OK,MessageBoxImage.Information);
    string[] PickAudio(bool multi=false)
    {
        var dialog = new OpenFileDialog { Filter=AudioFilter, Multiselect=multi, Title=multi?"添加干净人声素材":"导入音频",CheckFileExists=true };
        return dialog.ShowDialog(this)==true?dialog.FileNames:[];
    }
    static string[] Dropped(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) ? (string[])e.Data.GetData(DataFormats.FileDrop) : [];
    void LoadSettings()
    {
        root=Path.GetFullPath(Path.Combine(appDir,"AIEnvironment"));
        exportDir=Path.GetFullPath(Path.Combine(appDir,"exports"));
        try
        {
            var path=Path.Combine(DataDir,"settings.json");
            if(!File.Exists(path)) return;
            var settings=JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(path),jsonOptions);
            if(!string.IsNullOrWhiteSpace(settings?.EngineDirectory)) root=Path.GetFullPath(settings.EngineDirectory);
            if(!string.IsNullOrWhiteSpace(settings?.ExportDirectory)) exportDir=Path.GetFullPath(settings.ExportDirectory);
        }
        catch(Exception e) { Log("设置读取失败："+e.Message); }
    }
    void SaveSettings()
    {
        SaveAtomic(Path.Combine(DataDir,"settings.json"),JsonSerializer.Serialize(new StudioSettings { EngineDirectory=root,ExportDirectory=ExportDir },jsonOptions));
    }
    void AcceptFileDrop(object sender,DragEventArgs e) { e.Effects=!busy&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None; e.Handled=true; }
    void PickSplit() { var files=PickAudio(); if(files.Length>0) SetSplit(files[0]); }
    void SetSplit(string path)
    {
        if(!IsAudio(path)) { Notify("请选择 MP3、WAV、FLAC、M4A、AAC、OGG 或 WMA 音频。"); return; }
        splitInput=Path.GetFullPath(path); splitName.Text=Path.GetFileName(path); splitName.ToolTip=path;
        splitDetail.Text=$"{Path.GetExtension(path).ToUpperInvariant().TrimStart('.')} · {new FileInfo(path).Length/1024d/1024:0.0} MB · 已导入";
    }
    void PickCover() { var files=PickAudio(); if(files.Length>0) SetCover(files[0]); }
    void SetCover(string path) { if(!IsAudio(path)) { Notify("请选择支持的音频文件。"); return; } coverInput.Text=Path.GetFullPath(path); coverInput.ToolTip=path; }
    void PickVideo()
    {
        var dialog=new OpenFileDialog { Filter=VideoFilter,Title="导入视频",CheckFileExists=true };
        if(dialog.ShowDialog(this)==true) SetVideo(dialog.FileName);
    }
    void SetVideo(string path)
    {
        if(!IsVideo(path)) { Notify("请选择 MP4、MOV、MKV、WEBM、AVI、FLV、M4V 或 TS 视频文件。"); return; }
        var full=Path.GetFullPath(path);
        videoInput.Text=full; videoInput.ToolTip=full;
        videoName.Text=Path.GetFileName(path);
        videoName.ToolTip=full;
        videoDetail.Text=$"{Path.GetExtension(path).ToUpperInvariant().TrimStart('.')} · {new FileInfo(path).Length/1024d/1024:0.0} MB · 已导入";
    }
    void PickTraining() => AddTrainingFiles(PickAudio(true));    void AddTrainingFiles(IEnumerable<string> files)
    {
        foreach(var path in files.Where(IsAudio)) if(!trainingInputs.Contains(Path.GetFullPath(path),StringComparer.OrdinalIgnoreCase)) trainingInputs.Add(Path.GetFullPath(path));
        trainFiles.ItemsSource=trainingInputs.Select(x=>new FileItem(x)).ToList();
        trainingCount.Text=trainingInputs.Count==0?"尚未添加素材 · 可拖入多个音频文件":$"已添加 {trainingInputs.Count} 份素材 · 总大小 {trainingInputs.Sum(x=>new FileInfo(x).Length)/1024d/1024:0.0} MB";
    }
    void RemoveTraining() { if(trainFiles.SelectedItem is FileItem f) { trainingInputs.Remove(f.Path); AddTrainingFiles([]); } }
    void PickFolder(TextBox field)
    {
        var dialog=new OpenFolderDialog { Title="选择导出位置",InitialDirectory=Directory.Exists(ExportDir)?ExportDir:appDir };
        if(dialog.ShowDialog(this)==true)
        {
            exportDir=Path.GetFullPath(dialog.FolderName);
            SaveSettings();
            UpdateExportDisplays();
        }
    }
    void SetExportDisplay(TextBox field)
    {
        var defaultPath=Path.GetFullPath(Path.Combine(appDir,"exports"));
        var custom=!Path.GetFullPath(ExportDir).Equals(defaultPath,StringComparison.OrdinalIgnoreCase);
        field.Text=custom?"已选择自定义导出位置":"默认：软件目录下的 exports";
        field.ToolTip=ExportDir;
    }
    void UpdateExportDisplays()
    {
        foreach(var field in new[]{splitOutput,coverOutput,videoOutput})
            if(field!=null) SetExportDisplay(field);
    }
    void ChooseEngineDirectory()
    {
        var dialog=new OpenFolderDialog { Title="选择 AI 环境下载位置",InitialDirectory=Directory.Exists(root)?root:appDir };
        if(dialog.ShowDialog(this)!=true) return;
        root=Path.GetFullPath(dialog.FolderName);
        SaveSettings();
        UpdateEngineDirectoryDisplay();
    }
    void UpdateEngineDirectoryDisplay()
    {
        if(engineDirectoryBox==null) return;
        var defaultPath=Path.GetFullPath(Path.Combine(appDir,"AIEnvironment"));
        engineDirectoryBox.Text=Path.GetFullPath(root).Equals(defaultPath,StringComparison.OrdinalIgnoreCase)
            ?"默认：软件目录下的 AIEnvironment":"已选择自定义位置";
        engineDirectoryBox.ToolTip=root;
    }
    void OpenFolder(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) { Notify("尚未生成结果目录。"); return; }
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute=true,ArgumentList={path} });
    }
    bool Capability(string key) => diagnostic.ValueKind==JsonValueKind.Object && diagnostic.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.True;
    async Task<bool> EnsureCapabilityAsync(string key,string task)
    {
        if(Capability(key)) return true;
        SwitchPage("settings");
        var audioOnly=key=="audio_ready";
        var dialog=new EngineDownloadDialog(root,appDir,audioOnly) { Owner=this };
        if(dialog.ShowDialog()!=true) return false;
        root=Path.GetFullPath(dialog.EngineRoot);
        SaveSettings(); UpdateEngineDirectoryDisplay();
        await RunEngineSetupAsync(audioOnly);
        if(Capability(key)) return true;
        Notify(task+"所需的环境仍未就绪。请查看环境检查结果；可修复网络或磁盘问题后继续下载。");
        return false;
    }
    async Task StartSplitAsync()
    {
        if(!IsAudio(splitInput)) { Notify("请先导入需要分离的歌曲。"); return; }
        if(!await EnsureCapabilityAsync("split_ready","人声分离")) return;
        if(splitReverb.IsChecked==true && !await EnsureCapabilityAsync("dereverb_ready","去混响")) return;
        await RunWorkerAsync("split","人声与伴奏分离",new { input=splitInput,output_dir=ExportDir,bitrate=new[]{320,192,128}[bitrate.SelectedIndex],keep_wav=splitWav.IsChecked==true,dereverb=splitReverb.IsChecked==true });
    }
    async Task StartTrainingAsync()
    {
        if(trainingInputs.Count==0) { Notify("请先添加干净的人声训练素材。"); return; }
        if(string.IsNullOrWhiteSpace(voiceName.Text)) { Notify("请输入音色名称。"); return; }
        if(!await EnsureCapabilityAsync("train_ready","音色训练")) return;
        await RunWorkerAsync("train","训练音色 · "+voiceName.Text.Trim(),new { inputs=trainingInputs.ToArray(),name=voiceName.Text.Trim(),epochs=new[]{100,200,300}[epochs.SelectedIndex],batch_size=batch.SelectedIndex+1 });
    }
    async Task StartCoverAsync()
    {
        if(!IsAudio(coverInput.Text)) { Notify("请先导入原歌曲或纯人声。"); return; }
        if(voiceChoice.SelectedItem is not VoiceRecord voice) { Notify("请选择音色；可以到音色库训练或导入音色。"); return; }
        if(!await EnsureCapabilityAsync(vocalOnly.IsChecked==true?"convert_ready":"cover_ready","音色转换")) return;
        if(coverReverb.IsChecked==true && vocalOnly.IsChecked!=true && !await EnsureCapabilityAsync("dereverb_ready","去混响")) return;
        await RunWorkerAsync("cover","翻唱 · "+Path.GetFileNameWithoutExtension(coverInput.Text),new { input=coverInput.Text,voice_id=voice.Id,output_dir=ExportDir,pitch=(int)pitch.Value,index_rate=indexRate.Value,protect=.33,vocal_gain=vocalGain.Value,instrumental_gain=instGain.Value,keep_wav=coverWav.IsChecked==true,dereverb=coverReverb.IsChecked==true,vocal_only=vocalOnly.IsChecked==true,bitrate=320 });
    }
    async Task TranscodeAsync()
    {
        if(!await EnsureCapabilityAsync("audio_ready","MP3 导出")) return;
        var files=PickAudio(); if(files.Length==0) return;
        await RunWorkerAsync("transcode","MP3 导出 · "+Path.GetFileNameWithoutExtension(files[0]),new { input=files[0],output_dir=ExportDir,bitrate=320 });
    }
    async Task StartVideoAsync()
    {
        if(!IsVideo(videoInput.Text)) { Notify("请先导入需要提取音频的视频文件。"); return; }
        if(!await EnsureCapabilityAsync("audio_ready","视频转音频")) return;
        var name=Path.GetFileNameWithoutExtension(videoInput.Text);
        await RunWorkerAsync("video_transcode","视频转音频 · "+name,new { input=videoInput.Text,output_dir=ExportDir,bitrate=new[]{320,192,128}[videoBitrate.SelectedIndex],keep_wav=videoWav.IsChecked==true });
    }
    async Task ImportVoiceAsync()
    {
        var installed = diagnostic.ValueKind==JsonValueKind.Object && diagnostic.TryGetProperty("packages",out var pkgs) && pkgs.TryGetProperty("torch",out var t) && t.ValueKind==JsonValueKind.String;
        if(!installed && !await EnsureCapabilityAsync("convert_ready","导入音色")) return;
        installed = diagnostic.ValueKind==JsonValueKind.Object && diagnostic.TryGetProperty("packages",out pkgs) && pkgs.TryGetProperty("torch",out t) && t.ValueKind==JsonValueKind.String;
        if(!installed) { Notify("当前没有可用的 PyTorch。请先安装 AI 环境，再导入音色模型。"); return; }
        var file=new OpenFileDialog { Filter="RVC 音色模型|*.pth",Title="导入 RVC 推理音色模型" };
        if(file.ShowDialog(this)!=true) return;
        var dialog=new VoiceImportDialog(file.FileName) { Owner=this };
        if(dialog.ShowDialog()!=true) return;
        await RunWorkerAsync("import_voice","导入音色 · "+dialog.VoiceName,new { model=file.FileName,name=dialog.VoiceName,index=dialog.IndexPath });
    }
    async Task SearchVoiceCatalogAsync()
    {
        var dialog=new VoiceCatalogDialog(Path.Combine(appDir,"voices")) { Owner=this };
        if(dialog.ShowDialog()!=true || dialog.DownloadedVoice is not VoiceRecord voice) return;
        voiceRecords.Add(voice);
        SaveAtomic(Path.Combine(DataDir,"voices.json"),JsonSerializer.Serialize(voiceRecords,jsonOptions));
        LoadVoices();
        voiceList.SelectedItem=voiceRecords.FirstOrDefault(v=>v.Id==voice.Id);
    }
    void LoadVoices()
    {
        if(busy) return;
        string? chosen=(voiceChoice?.SelectedItem as VoiceRecord)?.Id;
        voiceRecords.Clear();
        var path=Path.Combine(DataDir,"voices.json");
        if(File.Exists(path))
        {
            try { voiceRecords.AddRange((JsonSerializer.Deserialize<List<VoiceRecord>>(File.ReadAllText(path),jsonOptions)??[]).Where(v=>File.Exists(v.Model))); }
            catch(Exception e) { Log("音色库读取失败："+e.Message); }
        }
        // Make existing engine models discoverable without moving or overwriting them.
        var engineModels=Path.Combine(root,"RVC","assets","weights");
        if(Directory.Exists(engineModels))
            foreach(var file in Directory.EnumerateFiles(engineModels,"*.pth"))
                if(!voiceRecords.Any(v=>Path.GetFullPath(v.Model).Equals(Path.GetFullPath(file),StringComparison.OrdinalIgnoreCase) || v.Id==Path.GetFileNameWithoutExtension(file) || v.Id=="discovered_"+Path.GetFileNameWithoutExtension(file)) && !Path.GetFileName(file).Contains("_e"))
                {
                    var name=Path.GetFileNameWithoutExtension(file);
                    var indices=Path.Combine(root,"RVC","assets","indices");
                    var match=Directory.Exists(indices)?Directory.EnumerateFiles(indices,"*.index").FirstOrDefault(x=>Path.GetFileName(x).StartsWith(name+"_")||Path.GetFileNameWithoutExtension(x)==name):null;
                    voiceRecords.Add(new VoiceRecord { Id="discovered_"+name,Name=name,Model=file,Index=match??"",Source="引擎音色",Created=File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm"),Version="RVC",SampleRate="自动识别" });
                }
        if(voiceList==null || voiceChoice==null) return;
        voiceList.ItemsSource=voiceRecords.ToList(); voiceChoice.ItemsSource=voiceRecords.ToList();
        voiceChoice.SelectedItem=voiceRecords.FirstOrDefault(v=>v.Id==chosen)??voiceRecords.FirstOrDefault();
        libraryEmpty.Visibility=voiceRecords.Count==0?Visibility.Visible:Visibility.Collapsed;
        voiceList.Visibility=voiceRecords.Count==0?Visibility.Collapsed:Visibility.Visible;
        // Preserve discovered entries so the Python worker can use them.
        SaveAtomic(path,JsonSerializer.Serialize(voiceRecords,jsonOptions));
    }
    void UseVoice()
    {
        if(voiceList.SelectedItem is not VoiceRecord v) { Notify("请先在音色库中选择一个音色。"); return; }
        voiceChoice.SelectedItem=voiceRecords.First(x=>x.Id==v.Id); SwitchPage("cover");
    }
    void ExportVoice()
    {
        if(busy) { Notify("请等待当前任务完成后再导出音色。"); return; }
        if(voiceList.SelectedItem is not VoiceRecord voice) { Notify("请先选择要导出的音色。"); return; }
        var dialog=new OpenFolderDialog { Title="选择音色文件导出目录",InitialDirectory=ExportDir };
        if(dialog.ShowDialog(this)!=true) return;
        var target=Path.Combine(dialog.FolderName,SafeFilename(voice.Name)+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")[..4]);
        Directory.CreateDirectory(target);
        File.Copy(voice.Model,Path.Combine(target,"音色.pth"),false);
        if(File.Exists(voice.Index)) File.Copy(voice.Index,Path.Combine(target,"音色.index"),false);
        File.WriteAllText(Path.Combine(target,"音色信息.json"),JsonSerializer.Serialize(voice,jsonOptions),Encoding.UTF8); OpenFolder(target);
    }
    static string SafeFilename(string value) { foreach(var c in Path.GetInvalidFileNameChars()) value=value.Replace(c,'_'); return value.Trim().TrimEnd('.') is { Length:>0 } s?s:"音色"; }
    static void SaveAtomic(string path,string text) { var tmp=path+".tmp"; File.WriteAllText(tmp,text,new UTF8Encoding(false)); File.Move(tmp,path,true); }
    void ReadHistory()
    {
        try
        {
            var path=Path.Combine(DataDir,"history.json");
            if(File.Exists(path)) history.AddRange(JsonSerializer.Deserialize<List<JobRecord>>(File.ReadAllText(path),jsonOptions)??[]);
            foreach(var job in history.Where(x=>x.Status=="运行中")) { job.Status="中断"; job.Error="应用关闭时任务尚未完成，可查看日志后重新执行。"; }
        }
        catch(Exception e) { Log("任务记录读取失败："+e.Message); }
        RefreshHistory();
    }
    void RefreshHistory()
    {
        if(historyList!=null) historyList.ItemsSource=history.OrderByDescending(x=>x.Created).ThenByDescending(x=>x.Id).ToList();
        SaveAtomic(Path.Combine(DataDir,"history.json"),JsonSerializer.Serialize(history,jsonOptions));
    }
    void SelectHistory()
    {
        if(historyList.SelectedItem is not JobRecord job) return;
        resultFolder=job.Folder; resultOutputs=job.Outputs;
        outputList.ItemsSource=resultOutputs.Where(File.Exists).Select(x=>new FileItem(x)).ToList();
        playerStatus.Text=job.Status=="已完成"?"选择音频开始试听":job.Status+" · "+job.Error;
    }
    void Play()
    {
        if(outputList.SelectedItem is not FileItem f || !IsAudio(f.Path)) { Notify("请选择 MP3 或 WAV 音频。"); return; }
        if(!File.Exists(f.Path)) { Notify("文件已经移动或删除。"); return; }
        player.Stop(); player.Source=new Uri(f.Path); player.Play(); playerStatus.Text="正在试听 · "+f.Name;
    }
    void SaveOutput()
    {
        if(outputList.SelectedItem is not FileItem f || !File.Exists(f.Path)) { Notify("请先选择一份导出文件。"); return; }
        var ext=Path.GetExtension(f.Path);
        var dialog=new SaveFileDialog { FileName=f.Name,Filter=$"导出文件 (*{ext})|*{ext}",DefaultExt=ext,OverwritePrompt=true };
        if(dialog.ShowDialog(this)==true && !Path.GetFullPath(f.Path).Equals(Path.GetFullPath(dialog.FileName),StringComparison.OrdinalIgnoreCase)) File.Copy(f.Path,dialog.FileName,true);
    }
    void SendToTraining()
    {
        if(outputList.SelectedItem is not FileItem f || !IsAudio(f.Path)) { Notify("请选择已分离的干净人声文件。"); return; }
        AddTrainingFiles([f.Path]); SwitchPage("train");
    }
    void OpenJobLog()
    {
        if(historyList.SelectedItem is JobRecord job && File.Exists(job.Log)) Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute=true,ArgumentList={job.Log} });
    }
    void Log(string message)
    {
        if(!Dispatcher.CheckAccess()) { Dispatcher.Invoke(()=>Log(message)); return; }
        if(activeJob!=null)
        {
            try { File.AppendAllText(activeJob.Log,message+Environment.NewLine,new UTF8Encoding(false)); } catch { }
        }
        var lines=taskLog.Text.Split('\n');
        if(lines.Length>180) taskLog.Text=string.Join('\n',lines.TakeLast(140));
        taskLog.AppendText(message+Environment.NewLine); taskLog.ScrollToEnd();
    }
    void SetBusy(bool value)
    {
        busy=value;
        taskRow.Height=new GridLength(value?166:104);
        foreach(var b in actionButtons) b.IsEnabled=!value;
        cancelButton.IsEnabled=value;
    }
    void CancelActive()
    {
        if(!busy) return;
        cancelRequested=true; taskTitle.Text="正在取消任务…";
        try
        {
            if(processJob!=null) processJob.Stop();
            else if(running is { HasExited:false }) running.Kill(entireProcessTree:true);
        }
        catch(Exception e) { Log("取消失败："+e.Message); }
    }
    ProcessStartInfo PythonInfo(params string[] arguments)
    {
        if(!File.Exists(Python)) throw new InvalidOperationException("AI 环境尚未安装；请在环境与设置中选择下载位置并安装。");
        var rvc=Path.Combine(root,"RVC");
        var psi=new ProcessStartInfo(Python) { WorkingDirectory=Directory.Exists(rvc)?rvc:root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        psi.Environment["PYTHONUTF8"]="1"; psi.Environment["PYTHONIOENCODING"]="utf-8";
        psi.Environment["AISOUND_ROOT"]=root;
        foreach(var arg in arguments) psi.ArgumentList.Add(arg); return psi;
    }
    async Task ProbeAsync()
    {
        if(busy || !await probeLock.WaitAsync(0)) return;
        try
        {
            if(!File.Exists(Python))
            {
                diagnostic=default;
                engineLabel.Text="●  AI 环境待安装";
                engineLabel.Foreground=Brush("#E9C88C");
                checksPanel.Children.Clear();
                checksPanel.Children.Add(Hint("桌面界面已就绪；使用 AI 功能时可选择是否下载所需环境。"));
                checksPanel.Children.Add(Hint("下载只会在确认后开始，位置可在此页面选择。"));
                return;
            }
            var psi=PythonInfo("-u",Path.Combine(appDir,"backend","bridge.py"),"diagnostics");
            using var proc=Process.Start(psi)!;
            var stdout=proc.StandardOutput.ReadToEndAsync(); var stderr=proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            foreach(var line in (await stdout).Split('\n',StringSplitOptions.RemoveEmptyEntries))
            {
                try { using var doc=JsonDocument.Parse(line); if(doc.RootElement.GetProperty("type").GetString()=="result") diagnostic=doc.RootElement.GetProperty("data").Clone(); } catch { }
            }
            if(proc.ExitCode!=0) throw new InvalidOperationException((await stderr).Trim());
            RenderChecks();
        }
        catch(Exception e)
        {
            engineLabel.Text="●  环境检测失败"; checksPanel.Children.Clear(); checksPanel.Children.Add(Hint(e.Message)); Log(e.Message);
        }
        finally { probeLock.Release(); }
    }
    void RenderChecks()
    {
        checksPanel.Children.Clear();
        if(diagnostic.ValueKind!=JsonValueKind.Object) return;
        foreach(var check in diagnostic.GetProperty("checks").EnumerateArray())
        {
            bool ok=check.GetProperty("ok").GetBoolean();
            var left=Txt((ok?"✓  ":"○  ")+check.GetProperty("name").GetString(),12,ok?"#B8EACD":"#D8BA8E");
            var right=Txt(check.GetProperty("detail").GetString()??"",11,"#96A7B8"); right.MaxWidth=430;
            var row=Spread(left,right); row.Margin=new Thickness(0,8,0,8); checksPanel.Children.Add(row);
        }
        engineLabel.Text=Capability("cover_ready")&&Capability("train_ready")?"●  AI 引擎已就绪":Capability("audio_ready")?"●  MP3 就绪 · AI 待配置":"●  引擎待配置";
        engineLabel.Foreground=Brush(Capability("cover_ready")?"#ADF0D1":"#E9C88C");
    }
    async Task RunWorkerAsync(string action,string title,object request)
    {
        var requestDir=Path.Combine(DataDir,"requests"); Directory.CreateDirectory(requestDir);
        var requestFile=Path.Combine(requestDir,Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(requestFile,JsonSerializer.Serialize(request,jsonOptions),new UTF8Encoding(false));
        await ExecuteAsync(title,PythonInfo("-u",Path.Combine(appDir,"backend","bridge.py"),action,requestFile),true);
    }
    async Task ExecuteAsync(string title,ProcessStartInfo psi,bool protocol)
    {
        if(busy) return;
        await probeLock.WaitAsync(); // Avoid model/catalog refresh racing with a mutating task.
        SetBusy(true); cancelRequested=false; progress.Value=0; progress.IsIndeterminate=!protocol; taskLog.Clear(); taskTitle.Text=title; taskPercent.Text="处理中";
        var logs=Path.Combine(DataDir,"logs"); Directory.CreateDirectory(logs);
        activeJob=new JobRecord { Title=title,Log=Path.Combine(logs,DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")[..6]+".log") };
        history.Add(activeJob); RefreshHistory();
        JsonElement result=default; string error=""; bool sawResult=false;
        try
        {
            running=Process.Start(psi) ?? throw new IOException("无法启动处理进程。");
            try { processJob=new ProcessJob(running); }
            catch(Exception e) { running.Kill(entireProcessTree:true); throw new IOException("无法建立任务进程管理，已停止任务："+e.Message,e); }
            async Task ReadLines(StreamReader reader,bool stdout)
            {
                while(await reader.ReadLineAsync() is { } line)
                {
                    await Dispatcher.InvokeAsync(()=>
                    {
                        if(protocol && stdout)
                        {
                            try
                            {
                                using var doc=JsonDocument.Parse(line); var ev=doc.RootElement; var type=ev.GetProperty("type").GetString();
                                if(type=="progress") { progress.IsIndeterminate=false; progress.Value=Math.Max(progress.Value,Math.Clamp(ev.GetProperty("value").GetDouble(),0,100)); taskPercent.Text=$"{progress.Value:0}%"; taskTitle.Text=ev.GetProperty("message").GetString(); }
                                else if(type=="result") { result=ev.GetProperty("data").Clone(); sawResult=true; }
                                else if(type=="error") { error=ev.GetProperty("message").GetString()??"任务失败"; Log(error); }
                                else if(type=="log") Log(ev.GetProperty("message").GetString()??"");
                                return;
                            } catch(JsonException) { } catch(KeyNotFoundException) { }
                        }
                        Log(line);
                    });
                }
            }
            var output=ReadLines(running.StandardOutput,true); var stderr=ReadLines(running.StandardError,false);
            await running.WaitForExitAsync(); await Task.WhenAll(output,stderr);
            if(cancelRequested) { activeJob.Status="已取消"; activeJob.Error="保留已保存文件与下载断点"; taskTitle.Text="任务已取消"; taskPercent.Text="已取消"; }
            else if(running.ExitCode!=0 || (protocol&&!sawResult))
            {
                activeJob.Status="失败"; activeJob.Error=error.Length>0?error:$"进程退出码 {running.ExitCode}；查看日志了解原因";
                taskTitle.Text="任务未完成 · "+title; taskPercent.Text="失败"; Log(activeJob.Error);
            }
            else
            {
                activeJob.Status="已完成"; taskTitle.Text=title+" · 已完成"; taskPercent.Text="100%"; progress.Value=100;
                if(protocol)
                {
                    if(result.TryGetProperty("folder",out var folder)) activeJob.Folder=folder.GetString()??"";
                    if(result.TryGetProperty("outputs",out var outputs)) activeJob.Outputs=outputs.EnumerateArray().Select(x=>x.GetString()!).ToArray();
                    resultFolder=activeJob.Folder; resultOutputs=activeJob.Outputs;
                    Log("结果已保存："+activeJob.Folder);
                }
            }
        }
        catch(Exception e)
        {
            activeJob.Status=cancelRequested?"已取消":"失败"; activeJob.Error=e.Message; taskTitle.Text=title+" · "+activeJob.Status; taskPercent.Text=activeJob.Status; Log(e.Message);
        }
        finally
        {
            progress.IsIndeterminate=false; var job=activeJob; processJob?.Dispose(); processJob=null; running?.Dispose(); running=null; activeJob=null;
            SetBusy(false); RefreshHistory(); LoadVoices(); probeLock.Release();
            if(job.Status=="已完成" && protocol) { SwitchPage("history"); historyList.SelectedItem=job; }
            await ProbeAsync();
        }
    }
    async Task InstallEngineAsync()
    {
        var dialog=new EngineDownloadDialog(root,appDir) { Owner=this };
        if(dialog.ShowDialog()!=true) return;
        root=Path.GetFullPath(dialog.EngineRoot);
        SaveSettings(); UpdateEngineDirectoryDisplay();
        await RunEngineSetupAsync();
    }
    async Task RunEngineSetupAsync(bool audioOnly=false)
    {
        var script=Path.Combine(appDir,"backend","setup_engine.ps1");
        if(!File.Exists(script)) { Notify("安装脚本缺失。请重新安装应用或从项目源码构建。"); return; }
        await ExecuteAsync(audioOnly?"安装音频工具":"AI 引擎安装 / 续传",ShellInfo(script,audioOnly),false);
    }
    ProcessStartInfo ShellInfo(string script,bool audioOnly=false)
    {
        var psi=new ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=appDir };
        foreach(var arg in new[]{"-NoProfile","-File",script,"-EngineRoot",root,"-AppDirectory",appDir}) psi.ArgumentList.Add(arg);
        if(audioOnly) psi.ArgumentList.Add("-AudioOnly");
        psi.Environment["PYTHONUTF8"]="1"; psi.Environment["PYTHONIOENCODING"]="utf-8"; return psi;
    }
    async Task InstallAudioAsync()
    {
        var dialog=new EngineDownloadDialog(root,appDir,true) { Owner=this };
        if(dialog.ShowDialog()!=true) return;
        root=Path.GetFullPath(dialog.EngineRoot);
        SaveSettings(); UpdateEngineDirectoryDisplay();
        await RunEngineSetupAsync(true);
    }
}

public sealed class StudioSettings
{
    public string EngineDirectory { get; set; } = "";
    public string ExportDirectory { get; set; } = "";
}

public class VoiceImportDialog : Window
{
    readonly TextBox name;
    readonly TextBox index;
    public string VoiceName => name.Text.Trim();
    public string IndexPath => index.Text.Trim();
    public VoiceImportDialog(string model)
    {
        Title="导入音色"; Width=510; Height=330; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(25,32,40)); Foreground=System.Windows.Media.Brushes.White;
        FontFamily=new System.Windows.Media.FontFamily("Microsoft YaHei UI"); FontSize=13;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("Theme.xaml",UriKind.Relative) });
        var panel=new StackPanel { Margin=new Thickness(24) };
        panel.Children.Add(new TextBlock { Text="音色名称",Margin=new Thickness(0,0,0,8) });
        name=new TextBox { Text=Path.GetFileNameWithoutExtension(model),MaxLength=40 }; panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text="检索索引（可选 .index）",Margin=new Thickness(0,17,0,8) });
        index=new TextBox { IsReadOnly=true };
        var same=Path.ChangeExtension(model,".index"); if(File.Exists(same)) index.Text=same;
        var dock=new DockPanel(); var pick=new Button { Content="选择",Margin=new Thickness(8,0,0,0),Padding=new Thickness(13,10,13,10) };
        pick.Click+=(_,_)=> { var d=new OpenFileDialog { Filter="RVC 检索索引|*.index",Title="选择与音色配套的索引" }; if(d.ShowDialog(this)==true) index.Text=d.FileName; };
        DockPanel.SetDock(pick,Dock.Right); dock.Children.Add(pick); dock.Children.Add(index); panel.Children.Add(dock);
        panel.Children.Add(new TextBlock { Text="模型会校验后复制到本地音色库，原文件保留。",Foreground=System.Windows.Media.Brushes.LightGray,FontSize=11,Margin=new Thickness(0,12,0,18) });
        var confirm=new Button { Content="导入并保存",HorizontalAlignment=HorizontalAlignment.Right };
        confirm.Click+=(_,_)=> { if(VoiceName.Length>0) DialogResult=true; }; panel.Children.Add(confirm); Content=panel;
    }
}
