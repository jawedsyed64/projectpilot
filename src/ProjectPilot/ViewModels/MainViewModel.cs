using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
    public bool IsComingSoon => Phase > 1;
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

    public string VersionText => "Version 0.1.0  ·  Phase 1";

    public MainViewModel()
    {
        SelectedNav = NavItems[0];
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value == null) return;
        var page = GetPage(value);
        page.Refresh();
        CurrentPage = page;
    }

    private ViewModelBase GetPage(NavItem nav)
    {
        if (_pages.TryGetValue(nav.Key, out var existing)) return existing;
        ViewModelBase page = nav.Key switch
        {
            "dashboard" => new DashboardViewModel(),
            "projects" => new ProjectsViewModel(OpenTasksForProject),
            "tasks" => new TasksViewModel(),
            "settings" => new SettingsViewModel(),
            _ => ComingSoonViewModel.For(nav),
        };
        _pages[nav.Key] = page;
        return page;
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
