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

        // A background tray app has no console and no window to show the default WinForms crash
        // dialog usefully — log and keep running instead of dying on an unexpected exception.
        TrayApplicationContext? context = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleUnhandledException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HandleUnhandledException(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown fatal error"));

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("KARTCompanion/1.0 (+https://github.com/)");

        ISimReportFetcher[] simFetchers =
        {
            new RaidbotsReportClient(httpClient),
            new QELiveReportClient(httpClient),
        };

        using (context = new TrayApplicationContext(httpClient, simFetchers))
        {
            Application.Run(context);
        }

        void HandleUnhandledException(Exception ex)
        {
            CrashLog.Log(ex);
            context?.ShowUnexpectedError(ex.Message);
        }
    }
}
