using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using ProjectPilot.Data;
using ProjectPilot.Models;
using ProjectPilot.ViewModels;
using ProjectPilot.Views;

namespace ProjectPilot.Services;

public static class AppServices
{
    public static Database Db { get; set; } = null!;
    public static DataService Data { get; set; } = null!;
}

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        var name = theme == "Dark" ? "Dark" : "Light";
        var dicts = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{name}.xaml", UriKind.Absolute)
        };
        var existing = dicts.FirstOrDefault(d => d.Source != null &&
            (d.Source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)));
        if (existing != null) dicts[dicts.IndexOf(existing)] = replacement;
        else dicts.Insert(0, replacement);
    }
}

public static class Dialogs
{
    private static Window? ActiveWindow =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    public static bool Confirm(string message, string title = "Please confirm")
    {
        var o = ActiveWindow;
        var r = o != null
            ? MessageBox.Show(o, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return r == MessageBoxResult.Yes;
    }

    public static void Info(string message, string title = "ProjectPilot")
    {
        var o = ActiveWindow;
        if (o != null) MessageBox.Show(o, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static void Error(string message, string title = "ProjectPilot")
    {
        var o = ActiveWindow;
        if (o != null) MessageBox.Show(o, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        else MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public static bool EditProject(Project? project)
    {
        var vm = new ProjectEditorViewModel(project);
        var w = new ProjectEditorWindow { DataContext = vm, Owner = ActiveWindow };
        vm.CloseRequested += ok => w.DialogResult = ok;
        return w.ShowDialog() == true;
    }

    public static bool EditTask(TaskItem? task, int? projectId = null, int? parentId = null)
    {
        var vm = new TaskEditorViewModel(task, projectId, parentId);
        var w = new TaskEditorWindow { DataContext = vm, Owner = ActiveWindow };
        vm.CloseRequested += ok => w.DialogResult = ok;
        return w.ShowDialog() == true;
    }

    public static string[] PickFiles()
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Choose files to attach" };
        return dlg.ShowDialog() == true ? dlg.FileNames : Array.Empty<string>();
    }

    public static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) { Error("The file could not be found:\n" + path); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Error("Could not open the file:\n" + ex.Message); }
    }

    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Error("Could not open the folder:\n" + ex.Message); }
    }
}

public static class Metrics
{
    /// <summary>Project completion = average progress of leaf tasks (tasks without subtasks).</summary>
    public static int ProjectProgress(Project p, IReadOnlyCollection<TaskItem> projectTasks)
    {
        if (p.Status == ProjectStatus.Completed) return 100;
        var parentIds = projectTasks.Where(t => t.ParentId.HasValue).Select(t => t.ParentId!.Value).ToHashSet();
        var leaves = projectTasks.Where(t => !parentIds.Contains(t.Id)).ToList();
        if (leaves.Count == 0) return 0;
        return (int)Math.Round(leaves.Average(t => (double)t.EffectiveProgress));
    }
}
