using KARTCompanion.Simulations;
using KARTCompanion.Simulations.QELive;
using KARTCompanion.Simulations.Raidbots;

namespace KARTCompanion;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(true, "KARTCompanion.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("KART Companion is already running (check your system tray).",
                "KART Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("KARTCompanion/1.0 (+https://github.com/)");

        ISimReportFetcher[] simFetchers =
        {
            new RaidbotsReportClient(httpClient),
            new QELiveReportClient(httpClient),
        };

        using var context = new TrayApplicationContext(httpClient, simFetchers);
        Application.Run(context);
    }
}
