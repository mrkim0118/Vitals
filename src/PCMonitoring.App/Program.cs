using System;
using System.Windows.Forms;
using Velopack;

namespace PCMonitoring.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
