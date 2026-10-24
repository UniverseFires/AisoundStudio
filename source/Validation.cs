using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AisoundStudio;
public partial class MainWindow
{
    async Task HandleValidationArgumentsAsync()
    {
        var args=Environment.GetCommandLineArgs();
        int render=Array.IndexOf(args,"--render-dir");
        int audio=Array.IndexOf(args,"--test-audio");
        if(render<0 && audio<0) return;
        try
        {
            if(audio>=0 && audio+1<args.Length)
            {
                var target=Path.Combine(DataDir,"validation-exports");
                await RunWorkerAsync("transcode","验证 · 真实 MP3 导出",new { input=args[audio+1],output_dir=target,bitrate=320 });
                if(history.LastOrDefault()?.Status!="已完成") throw new InvalidOperationException("MP3 UI integration test failed.");
                // Test failed operations do not get recorded as successful.
                await RunWorkerAsync("transcode","验证 · 缺失输入",new { input=Path.Combine(DataDir,"nonexistent.wav"),output_dir=target,bitrate=320 });
                if(history.LastOrDefault()?.Status!="失败") throw new InvalidOperationException("Failure propagation test failed.");
                // Cancellation must also terminate the worker's descendants.
                var psi=new System.Diagnostics.ProcessStartInfo(Python)
                {
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
                    WorkingDirectory=Path.Combine(root,"RVC")
                };
                psi.ArgumentList.Add("-u"); psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("import subprocess,sys,time; p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(60)']); print('CHILD='+str(p.pid),flush=True); time.sleep(60)");
                var execution=ExecuteAsync("验证 · 取消进程树",psi,false);
                await Task.Delay(1200);
                int child=0;
                var childLine=taskLog.Text.Split('\n').FirstOrDefault(x=>x.StartsWith("CHILD="));
                if(childLine!=null) int.TryParse(childLine[6..].Trim(),out child);
                CancelActive();
                if(await Task.WhenAny(execution,Task.Delay(5000))!=execution) throw new InvalidOperationException("Cancellation exceeded 5 seconds.");
                await execution;
                if(history.LastOrDefault()?.Status!="已取消") throw new InvalidOperationException("Cancellation state test failed.");
                if(child>0)
                {
                    try { using var childProcess=System.Diagnostics.Process.GetProcessById(child); if(!childProcess.HasExited) throw new InvalidOperationException("Child process leaked after cancellation."); }
                    catch(ArgumentException) { }
                }
                File.WriteAllText(Path.Combine(DataDir,"ui-verification.json"),JsonSerializer.Serialize(new { mp3_export="passed",failure_reporting="passed",cancel_process_tree="passed",ai_song_processing="not_tested_missing_models" },jsonOptions));
            }
            if(render>=0 && render+1<args.Length)
            {
                Directory.CreateDirectory(args[render+1]);
                foreach(var key in pages.Keys)
                {
                    SwitchPage(key); UpdateLayout(); await Task.Delay(150);
                    var visual=(FrameworkElement)Content;
                    var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);
                    bitmap.Render(visual); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file=File.Create(Path.Combine(args[render+1],key+".png")); encoder.Save(file);
                }
                File.WriteAllText(Path.Combine(args[render+1],"render-status.json"),"{\"pages\":6,\"status\":\"passed\"}");
            }
        }
        catch(Exception e)
        {
            File.WriteAllText(Path.Combine(DataDir,"validation-error.txt"),e.ToString());
        }
        Close();
    }
}
