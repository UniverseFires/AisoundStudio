using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace AisoundStudio;
public partial class MainWindow
{
    void RenameVoice()
    {
        if (busy) { Notify("请等待当前任务完成后再重命名。"); return; }
        if (voiceList.SelectedItem is not VoiceRecord voice) { Notify("请先选择一个音色。"); return; }
        var dialog = new Window { Title="重命名音色", Owner=this, Width=520, Height=245,
            ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=Brush("#192028"), Foreground=Brush("#F0F4F8"), FontFamily=FontFamily };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("Theme.xaml",UriKind.Relative) });
        var input = new TextBox { Text=voice.Name, MaxLength=64, Margin=new Thickness(0,12,0,10) };
        var save = Btn("保存名称",()=>dialog.DialogResult=true);
        save.IsDefault=true; save.HorizontalAlignment=HorizontalAlignment.Right; save.Margin=new Thickness(0,16,0,0);
        dialog.Content=new StackPanel { Margin=new Thickness(24), Children={
            Txt("音色名称",15,"#F0F4F8",true),input,
            Hint("模型、索引文件和音色文件夹将同步改名。"),save } };
        dialog.Loaded+=(_,_)=> { input.Focus(); input.SelectAll(); };
        if (dialog.ShowDialog()!=true) return;
        var name=input.Text.Trim();
        if (name.Length==0 || name.Length>64 || name is "." or ".." || name.EndsWith('.') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 ||
            System.Text.RegularExpressions.Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        { Notify("请输入有效名称，不要使用斜杠、冒号、末尾句点或 Windows 保留名称。"); return; }
        if (name==voice.Name) return;
        var library=Path.GetFullPath(Path.Combine(appDir,"voices"));
        Directory.CreateDirectory(library);
        var oldName=voice.Name; var oldModel=voice.Model; var oldIndex=voice.Index;
        var oldFolder=Path.GetDirectoryName(Path.GetFullPath(oldModel))!;
        var destination=Path.Combine(library,name);
        var owned=string.Equals(Path.GetDirectoryName(oldFolder),library,StringComparison.OrdinalIgnoreCase)
            && (oldIndex.Length==0 || string.Equals(Path.GetDirectoryName(Path.GetFullPath(oldIndex)),oldFolder,StringComparison.OrdinalIgnoreCase))
            && !voiceRecords.Any(v=>v.Id!=voice.Id && string.Equals(Path.GetDirectoryName(Path.GetFullPath(v.Model)),oldFolder,StringComparison.OrdinalIgnoreCase));
        if (voiceRecords.Any(v=>v.Id!=voice.Id && string.Equals(v.Name,name,StringComparison.OrdinalIgnoreCase)) ||
            File.Exists(destination) || (Directory.Exists(destination) && !(owned && string.Equals(oldFolder,destination,StringComparison.OrdinalIgnoreCase))))
        { Notify("该名称或文件夹已存在，请换一个名称。"); return; }
        var staging=Path.Combine(library,".rename-"+Guid.NewGuid().ToString("N"));
        var undo=new Stack<Action>();
        void MoveFile(string from,string to)
        {
            if (from==to) return;
            var middle=Path.Combine(staging,Guid.NewGuid().ToString("N")+Path.GetExtension(from));
            File.Move(from,middle); undo.Push(()=>File.Move(middle,from));
            File.Move(middle,to); undo.Push(()=>File.Move(to,middle));
        }
        try
        {
            if (owned) { Directory.Move(oldFolder,staging); undo.Push(()=>Directory.Move(staging,oldFolder)); }
            else
            {
                Directory.CreateDirectory(staging);
                File.Copy(oldModel,Path.Combine(staging,Path.GetFileName(oldModel)));
                if (oldIndex.Length>0) File.Copy(oldIndex,Path.Combine(staging,Path.GetFileName(oldIndex)));
            }
            MoveFile(Path.Combine(staging,Path.GetFileName(oldModel)),Path.Combine(staging,name+".pth"));
            if (oldIndex.Length>0) MoveFile(Path.Combine(staging,Path.GetFileName(oldIndex)),Path.Combine(staging,name+".index"));
            Directory.Move(staging,destination); undo.Push(()=>Directory.Move(destination,staging));
            voice.Name=name; voice.Model=Path.Combine(destination,name+".pth");
            voice.Index=oldIndex.Length>0?Path.Combine(destination,name+".index"):"";
            SaveAtomic(Path.Combine(DataDir,"voices.json"),JsonSerializer.Serialize(voiceRecords,jsonOptions));
        }
        catch (Exception error)
        {
            voice.Name=oldName; voice.Model=oldModel; voice.Index=oldIndex;
            while (undo.TryPop(out var rollback)) { try { rollback(); } catch(Exception recovery) { Log("重命名回退："+recovery.Message); } }
            throw new IOException("重命名未完成："+error.Message,error);
        }
        var id=voice.Id;
        LoadVoices(); voiceList.SelectedItem=voiceRecords.FirstOrDefault(v=>v.Id==id);
        Log("音色已重命名："+name+"；文件夹："+destination);
    }
}
