using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ProjectPilot.Data;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public sealed record FeatureAvailability(string Feature, string WorksWhere);

public partial class SettingsViewModel : ViewModelBase
{
    private bool _loading;

    public List<string> Themes { get; } = new() { "Light", "Dark" };
    [ObservableProperty] private string _selectedTheme = "Light";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _status = "";

    public string DataFolder => AppPaths.DataDir;
    public string DatabaseFile => AppPaths.DbPath;

    public List<FeatureAvailability> Features { get; } = new()
    {
        new("Dashboard, projects, tasks, subtasks, dependencies", "Fully offline — on this PC"),
        new("Comments, attachments, activity history", "Fully offline — on this PC"),
        new("Backups, restore, light/dark theme", "Fully offline — on this PC"),
        new("Timeline / Gantt, milestones, calendar, reports (Phases 2 & 4)", "Offline — on this PC"),
        new("AI Project Assistant (Phase 3)", "Needs internet + your own AI API key (pay-as-you-go)"),
    };

    public override void Refresh()
    {
        _loading = true;
        SelectedTheme = Data.GetSetting("ui.theme") ?? "Light";
        UserName = Data.CurrentUser;
        Status = "";
        _loading = false;
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_loading) return;
        ThemeManager.Apply(value);
        Data.SetSetting("ui.theme", value);
    }

    partial void OnUserNameChanged(string value)
    {
        if (_loading) return;
        Data.SetSetting("user.name", value.Trim());
    }

    [RelayCommand]
    private void OpenDataFolder() => Dialogs.OpenFolder(AppPaths.DataDir);

    [RelayCommand]
    private void OpenBackupsFolder() => Dialogs.OpenFolder(AppPaths.BackupsDir);

    [RelayCommand]
    private void BackupNow()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save a backup of all ProjectPilot data",
            Filter = "ProjectPilot backup (*.db)|*.db",
            FileName = $"ProjectPilot-backup-{DateTime.Now:yyyy-MM-dd}.db",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
            Data.BackupTo(dlg.FileName);
            Status = $"Backup saved to {dlg.FileName}";
        }
        catch (Exception ex) { Dialogs.Error("Backup failed:\n" + ex.Message); }
    }

    [RelayCommand]
    private void Restore()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a ProjectPilot backup to restore",
            Filter = "ProjectPilot backup (*.db)|*.db|All files (*.*)|*.*",
            InitialDirectory = AppPaths.BackupsDir,
        };
        if (dlg.ShowDialog() != true) return;
        if (!Dialogs.Confirm("Restoring replaces ALL current projects and tasks with the contents of the backup.\n\n" +
                             "A safety copy of your current data will be saved in the backups folder first.\n\nContinue?",
                "Restore backup")) return;
        try
        {
            Data.RestoreFrom(dlg.FileName);
            Refresh();
            ThemeManager.Apply(SelectedTheme);
            Status = "Backup restored successfully.";
        }
        catch (Exception ex) { Dialogs.Error("That file could not be restored. It may not be a ProjectPilot backup.\n\n" + ex.Message); }
    }
}
