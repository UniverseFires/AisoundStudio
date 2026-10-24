using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AisoundStudio;
public partial class App : Application
{
    Mutex? instance;
    internal static void StartupLog(string message)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"), $"{DateTime.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
        }
        catch { }
    }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    protected override void OnStartup(StartupEventArgs e)
    {
        StartupLog("OnStartup begin");
        base.OnStartup(e);
        StartupLog("base.OnStartup complete");
        var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory)))[..20];
        instance=new Mutex(true,"Local\\AisoundStudio_v2_"+identity,out bool created);
        StartupLog($"mutex created={created}");
        if(!created)
        {
            bool focused=false;
            foreach(var process in Process.GetProcessesByName("AisoundStudio"))
            {
                if(process.Id!=Environment.ProcessId && process.MainWindowHandle!=IntPtr.Zero) { ShowWindow(process.MainWindowHandle,9); SetForegroundWindow(process.MainWindowHandle); focused=true; break; }
            }
            if(focused) { Shutdown(); return; }
            StartupLog("Existing instance has no window; opening recovery window");
        }
        StartupLog("MainWindow construction begin");
        var window = new MainWindow();
        StartupLog("MainWindow construction complete");
        window.Show();
        StartupLog("MainWindow shown");
    }
    protected override void OnExit(ExitEventArgs e)
    {
        try { instance?.ReleaseMutex(); } catch(ApplicationException) { }
        instance?.Dispose(); base.OnExit(e);
    }
    public App()
    {
        StartupLog("App constructor");
        DispatcherUnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "data")); File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "data", "crash.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { }
            MessageBox.Show(e.Exception.Message, "声屿 · 出现问题", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
    }
}
