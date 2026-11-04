using System.IO;
using System.Threading;
using System.Windows;

namespace DeepSeekPet;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log(a.ExceptionObject?.ToString() ?? "unknown");
        DispatcherUnhandledException += (_, a) => { Log(a.Exception.ToString()); a.Handled = true; };

        string[] args = e.Args ?? Array.Empty<string>();

        // 命令行开关：给「设置/取消开机自启.bat」用，不弹窗口
        if (args.Contains("--autostart-on") || args.Contains("--autostart-off"))
        {
            bool want = args.Contains("--autostart-on");
            bool ok = Autostart.SetEnabled(want);
            Environment.Exit(ok ? 0 : 1);
            return;
        }

        if (args.Contains("--autostart-status"))
        {
            Environment.Exit(Autostart.IsEnabled() ? 0 : 1);
            return;
        }

        bool created;
        _mutex = new Mutex(true, "DeepSeekPet.SingleInstance", out created);
        if (!created)
        {
            MessageBox.Show("大肥鱼已经在桌面上了，找找她？", "大肥鱼", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    internal static void Log(string message)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "pet-error.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}
