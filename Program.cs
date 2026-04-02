using System;
using System.Windows.Forms;

namespace AprsScraper
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Check if launched by Windows Startup to start silently in the tray
            bool startSilent = args.Length > 0 && args[0] == "--silent";
            Application.Run(new MainForm(startSilent));
        }
    }
}