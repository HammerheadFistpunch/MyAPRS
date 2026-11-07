using System.ServiceProcess;

namespace MyAPRS;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--service"))
        {
            using var service = new LoggerService();
            ServiceBase.Run(service);
            return;
        }

        using var instance = new Mutex(true, @"Local\MyAPRS.Tray", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplication(args.Contains("--tray")));
    }
}
