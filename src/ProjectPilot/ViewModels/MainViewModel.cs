using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPilot.Data;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected DataService Data => AppServices.Data;

    /// <summary>Reload everything shown on this page from the database.</summary>
    public abstract void Refresh();
}

public sealed record NavItem(string Key, string Title, string Icon, int Phase)
{
    public const int CurrentPhase = 2;
    public bool IsComingSoon => Phase > CurrentPhase;
}

public partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<string, ViewModelBase> _pages = new();

    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new("dashboard", "Dashboard", "", 1),
        new("projects", "My Projects", "", 1),
        new("tasks", "Tasks", "", 1),
        new("timeline", "Timeline & Gantt", "", 2),
        new("milestones", "Milestones", "", 2),
        new("team", "Team Management", "", 4),
        new("ai", "AI Project Assistant", "", 3),
        new("reports", "Reports & Analytics", "\uE8A5", 4),
        new("calendar", "Calendar", "", 4),
        new("settings", "Settings", "", 1),
    };

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private ViewModelBase? _currentPage;

    public string VersionText => "Version 0.2.0  ·  Phase 2";

    // ---- deadline alert banner ----
    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private string _bannerKey = "Warning";
    [ObservableProperty] private bool _showBanner;
    private string _dismissedText = "";
    private readonly DispatcherTimer _alertTimer;

    public MainViewModel()
    {
        SelectedNav = NavItems[0];
        CheckAlerts();
        _alertTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _alertTimer.Tick += (_, _) => CheckAlerts();
        _alertTimer.Start();
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value == null) return;
        var page = GetPage(value);
        page.Refresh();
        CurrentPage = page;
        CheckAlerts();
    }

    /// <summary>Automatic alert when deadlines are near or work has fallen behind.</summary>
    public void CheckAlerts()
    {
        try
        {
            var data = AppServices.Data;
            var tasks = data.GetTasks();
            var milestones = data.GetMilestones();
            var today = DateTime.Today;
            int overdue = tasks.Count(t => t.IsOverdue);
            int dueToday = tasks.Count(t => !t.IsCompleted && t.DueDate?.Date == today);
            int dueTomorrow = tasks.Count(t => !t.IsCompleted && t.DueDate?.Date == today.AddDays(1));
            int lateMs = milestones.Count(m => m.IsOverdue);
            int soonMs = milestones.Count(m => !m.IsCompleted && m.DueDate.HasValue && m.DueDate.Value.Date >= today && m.DueDate.Value.Date <= today.AddDays(3));

            var parts = new List<string>();
            if (overdue > 0) parts.Add($"{overdue} overdue task{(overdue == 1 ? "" : "s")}");
            if (dueToday > 0) parts.Add($"{dueToday} due today");
            if (dueTomorrow > 0) parts.Add($"{dueTomorrow} due tomorrow");
            if (lateMs > 0) parts.Add($"{lateMs} overdue milestone{(lateMs == 1 ? "" : "s")}");
            if (soonMs > 0) parts.Add($"{soonMs} milestone{(soonMs == 1 ? "" : "s")} in the next 3 days");

            var text = parts.Count == 0 ? "" : string.Join("  ·  ", parts);
            BannerKey = overdue > 0 || lateMs > 0 ? "Danger" : "Warning";
            BannerText = text;
            ShowBanner = text.Length > 0 && text != _dismissedText;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    [RelayCommand]
    private void DismissBanner()
    {
        _dismissedText = BannerText;
        ShowBanner = false;
    }

    [RelayCommand]
    private void OpenDashboard()
    {
        var nav = NavItems.First(n => n.Key == "dashboard");
        if (SelectedNav == nav) CurrentPage?.Refresh();
        else SelectedNav = nav;
    }

    private ViewModelBase GetPage(NavItem nav)
    {
        if (_pages.TryGetValue(nav.Key, out var existing)) return existing;
        ViewModelBase page = nav.Key switch
        {
            "dashboard" => new DashboardViewModel(),
            "projects" => new ProjectsViewModel(OpenTasksForProject, OpenTimelineForProject),
            "tasks" => new TasksViewModel(),
            "timeline" => new TimelineViewModel(),
            "milestones" => new MilestonesViewModel(),
            "settings" => new SettingsViewModel(),
            _ => ComingSoonViewModel.For(nav),
        };
        _pages[nav.Key] = page;
        return page;
    }

    public void OpenTimelineForProject(int projectId)
    {
        var nav = NavItems.First(n => n.Key == "timeline");
        var page = (TimelineViewModel)GetPage(nav);
        page.PresetProject(projectId);
        if (SelectedNav == nav) page.Refresh();
        else SelectedNav = nav;
    }

    public void OpenTasksForProject(int projectId)
    {
        var nav = NavItems.First(n => n.Key == "tasks");
        var tasks = (TasksViewModel)GetPage(nav);
        tasks.PresetProject(projectId);
        if (SelectedNav == nav) tasks.Refresh();
        else SelectedNav = nav;
    }
}

public sealed class ComingSoonViewModel : ViewModelBase
{
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "";
    public string PhaseText { get; init; } = "";
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> Planned { get; init; } = Array.Empty<string>();

    public override void Refresh() { }

    public static ComingSoonViewModel For(NavItem nav)
    {
        var (summary, planned) = nav.Key switch
        {
            "timeline" => ("Interactive timeline with drag-to-reschedule.", new[]
            {
                "Gantt chart of every task and milestone", "Drag tasks to move or resize them",
                "Dependent tasks shift automatically", "Critical path highlighting", "Planned vs. actual dates",
            }),
            "milestones" => ("Key checkpoints for each project.", new[]
            {
                "Create milestones with dates and linked tasks", "Milestone completion tracking",
                "Milestones shown on the dashboard, timeline and calendar",
            }),
            "team" => ("Plan who does what and keep workloads balanced.", new[]
            {
                "Team member profiles with roles, skills and availability", "Workload view per person",
                "Overload warnings and quick reassignment", "Individual contribution tracking",
            }),
            "ai" => ("An AI partner that really analyses your projects (needs internet and an API key).", new[]
            {
                "Turn a project description into a full plan", "Smart prioritisation with reasons",
                "Next-step recommendations and delay warnings", "Weekly and monthly AI reports",
                "Every important change is shown to you for approval first",
                "Choice of Claude, OpenAI or Google Gemini — API key stored encrypted",
            }),
            "reports" => ("Insights and exports.", new[]
            {
                "Progress, task, milestone and deadline reports", "Team workload and delay analysis",
                "Budget tracking", "Export to PDF, Excel and CSV",
            }),
            "calendar" => ("Everything with a date, in one calendar.", new[]
            {
                "Deadlines, task due dates and milestones", "Meetings and team activities",
                "Configurable reminders and Windows notifications",
            }),
            _ => ("", Array.Empty<string>()),
        };
        return new ComingSoonViewModel
        {
            Title = nav.Title,
            Icon = nav.Icon,
            PhaseText = $"Coming in Phase {nav.Phase}",
            Summary = summary,
            Planned = planned,
        };
    }
}
