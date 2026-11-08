using System.Windows;
using System.Windows.Threading;
using ProjectPilot.Data;
using ProjectPilot.Services;
using ProjectPilot.ViewModels;

namespace ProjectPilot;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => LogError(a.ExceptionObject as Exception);

        try
        {
            AppPaths.Ensure();
            var db = new Database(AppPaths.DbPath);
            db.Migrate();
            AppServices.Db = db;
            AppServices.Data = new DataService(db);
            try { AppServices.Data.AutoBackup(); } catch (Exception ex) { LogError(ex); }
            ThemeManager.Apply(AppServices.Data.GetSetting("ui.theme") ?? "Light");
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show("ProjectPilot could not open its database.\n\n" + ex.Message +
                            "\n\nData folder: " + AppPaths.DataDir, "ProjectPilot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow { DataContext = new MainViewModel() };
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show("Something went wrong, but your data is safe.\n\n" + e.Exception.Message +
                        "\n\nDetails were saved to:\n" + AppPaths.ErrorLog,
            "ProjectPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    public static void LogError(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(AppPaths.ErrorLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* never crash while logging */ }
    }
}
