using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPilot.Converters;
using ProjectPilot.Models;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public sealed record StatCard(string Title, string Value, string Caption, string AccentKey);

public sealed record ProjectProgressRow(string Name, int Progress, ProjectStatus Status, string TargetText, bool IsLate,
    ProjectHealth Health, string HealthReason)
{
    public string ProgressText => $"{Progress}%";
}

public sealed record StatusBarRow(WorkStatus Status, int Count, int Max)
{
    public string Label => EnumDisplayConverter.Humanize(Status.ToString());
}

public sealed record WorkloadRow(string Name, int Open, int Overdue, int Max)
{
    public string Caption => Overdue > 0 ? $"{Open} open · {Overdue} overdue" : $"{Open} open";
}

/// <summary>Rule-based alert (not AI). AI recommendations arrive in Phase 3.</summary>
public sealed record AlertRow(string Severity, string Message);

public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty] private List<StatCard> _stats = new();
    [ObservableProperty] private List<ProjectProgressRow> _projectProgress = new();
    [ObservableProperty] private List<StatusBarRow> _statusBars = new();
    [ObservableProperty] private List<TaskItem> _upcoming = new();
    [ObservableProperty] private List<TaskItem> _overdue = new();
    [ObservableProperty] private List<WorkloadRow> _workload = new();
    [ObservableProperty] private List<AlertRow> _alerts = new();
    [ObservableProperty] private List<Milestone> _milestones = new();
    [ObservableProperty] private string _todayText = "";

    [RelayCommand]
    private void Reload() => Refresh();

    public override void Refresh()
    {
        var today = DateTime.Today;
        TodayText = today.ToString("dddd, d MMMM yyyy");
        var projects = Data.GetProjects();
        var tasks = Data.GetTasks();
        var allMilestones = Data.GetMilestones();
        var msByProject = allMilestones.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<Milestone>)g.ToList());
        var byProject = tasks.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<TaskItem>)g.ToList());
        IReadOnlyCollection<TaskItem> TasksOf(int id) => byProject.TryGetValue(id, out var l) ? l : Array.Empty<TaskItem>();

        int done = tasks.Count(t => t.IsCompleted);
        int open = tasks.Count - done;
        int overdue = tasks.Count(t => t.IsOverdue);
        int dueSoon = tasks.Count(t => !t.IsCompleted && t.DueDate.HasValue && t.DueDate.Value.Date >= today && t.DueDate.Value.Date <= today.AddDays(7));
        int pct = tasks.Count == 0 ? 0 : (int)Math.Round(100.0 * done / tasks.Count);

        Stats = new List<StatCard>
        {
            new("Total projects", projects.Count.ToString(), $"{projects.Count(p => p.Status == ProjectStatus.OnHold)} on hold", "Accent"),
            new("Active projects", projects.Count(p => p.Status == ProjectStatus.Active).ToString(), "in progress now", "Info"),
            new("Completed projects", projects.Count(p => p.Status == ProjectStatus.Completed).ToString(), "finished", "Success"),
            new("Pending projects", projects.Count(p => p.Status == ProjectStatus.Pending).ToString(), "not started yet", "Neutral"),
            new("Tasks completed", done.ToString(), $"{pct}% of all tasks", "Success"),
            new("Tasks outstanding", open.ToString(), $"of {tasks.Count} tasks", "Info"),
            new("Overdue tasks", overdue.ToString(), overdue == 0 ? "nothing overdue" : "need attention", overdue > 0 ? "Danger" : "Neutral"),
            new("Due in 7 days", dueSoon.ToString(), "upcoming deadlines", dueSoon > 0 ? "Warning" : "Neutral"),
        };

        ProjectProgress = projects
            .Where(p => p.Status != ProjectStatus.Cancelled)
            .Select(p =>
            {
                var progress = Metrics.ProjectProgress(p, TasksOf(p.Id));
                bool late = p.Status != ProjectStatus.Completed && p.TargetDate.HasValue && p.TargetDate.Value.Date < today;
                var target = p.TargetDate.HasValue ? "Target " + p.TargetDate.Value.ToString("dd MMM yyyy") : "No target date";
                var health = Tracking.Assess(p, TasksOf(p.Id), msByProject.TryGetValue(p.Id, out var pm) ? pm : Array.Empty<Milestone>());
                return new ProjectProgressRow(p.Name, progress, p.Status, late ? target + " — late" : target, late, health.Level, health.Reason);
            })
            .OrderBy(r => r.Status == ProjectStatus.Completed)
            .ThenByDescending(r => r.Health == ProjectHealth.Delayed)
            .ThenByDescending(r => r.Health == ProjectHealth.AtRisk)
            .ThenBy(r => r.Progress)
            .ToList();

        var counts = Enum.GetValues<WorkStatus>().Select(s => (s, n: tasks.Count(t => t.DisplayStatus == s))).ToList();
        int max = Math.Max(1, counts.Count == 0 ? 1 : counts.Max(x => x.n));
        StatusBars = counts.Select(x => new StatusBarRow(x.s, x.n, max)).ToList();

        Upcoming = tasks.Where(t => !t.IsCompleted && t.DueDate.HasValue && t.DueDate.Value.Date >= today && t.DueDate.Value.Date <= today.AddDays(14))
                        .OrderBy(t => t.DueDate).Take(10).ToList();
        Overdue = tasks.Where(t => t.IsOverdue).OrderBy(t => t.DueDate).Take(10).ToList();

        var load = tasks.Where(t => !t.IsCompleted)
                        .GroupBy(t => t.AssigneeDisplay)
                        .Select(g => (Name: g.Key, Open: g.Count(), Over: g.Count(t => t.IsOverdue)))
                        .OrderByDescending(x => x.Open).ToList();
        int maxLoad = Math.Max(1, load.Count == 0 ? 1 : load.Max(x => x.Open));
        Workload = load.Select(x => new WorkloadRow(x.Name, x.Open, x.Over, maxLoad)).ToList();

        Milestones = allMilestones.Where(m => !m.IsCompleted && (m.IsOverdue || (m.DueDate.HasValue && m.DueDate.Value.Date <= today.AddDays(30))))
                                  .OrderBy(m => m.DueDate).Take(8).ToList();

        var alerts = BuildAlerts(projects, tasks, TasksOf, today);
        var msAlerts = new List<AlertRow>();
        foreach (var m in allMilestones.Where(m => m.IsOverdue))
            msAlerts.Add(new("Danger", $"Milestone “{m.Title}” ({m.ProjectName}) was due {m.DueDate:dd MMM} and is not completed."));
        foreach (var m in allMilestones.Where(m => !m.IsCompleted && !m.IsOverdue && m.DueDate.HasValue &&
                                                   m.DueDate.Value.Date <= today.AddDays(3) && m.LinkedDone < m.LinkedTotal))
            msAlerts.Add(new("Warning", $"Milestone “{m.Title}” is due {m.DueDate:ddd dd MMM} with {m.LinkedTotal - m.LinkedDone} linked task(s) still open."));
        foreach (var m in allMilestones.Where(m => m.IsReady))
            msAlerts.Add(new("Info", $"All tasks for milestone “{m.Title}” are done — you can mark it completed."));
        if (msAlerts.Count > 0)
            alerts = alerts.Where(a => a.Severity != "Success").Concat(msAlerts)
                           .OrderBy(a => a.Severity switch { "Danger" => 0, "Warning" => 1, "Info" => 2, _ => 3 }).ToList();
        Alerts = alerts;
    }

    private static List<AlertRow> BuildAlerts(List<Project> projects, List<TaskItem> tasks,
        Func<int, IReadOnlyCollection<TaskItem>> tasksOf, DateTime today)
    {
        var alerts = new List<AlertRow>();
        foreach (var p in projects.Where(p => p.Status is ProjectStatus.Active or ProjectStatus.Pending or ProjectStatus.OnHold))
        {
            var pt = tasksOf(p.Id);
            var progress = Metrics.ProjectProgress(p, pt);
            if (p.TargetDate.HasValue && p.TargetDate.Value.Date < today)
                alerts.Add(new("Danger", $"“{p.Name}” passed its target date ({p.TargetDate:dd MMM}) and is {progress}% complete."));
            else if (p.TargetDate.HasValue && p.TargetDate.Value.Date <= today.AddDays(7) && progress < 80)
                alerts.Add(new("Warning", $"“{p.Name}” is due {p.TargetDate:dd MMM} but only {progress}% complete."));

            int od = pt.Count(t => t.IsOverdue);
            if (od > 0) alerts.Add(new("Danger", $"“{p.Name}” has {od} overdue task{(od == 1 ? "" : "s")}."));

            if (p.Status == ProjectStatus.Pending && p.StartDate.HasValue && p.StartDate.Value.Date < today)
                alerts.Add(new("Warning", $"“{p.Name}” was due to start on {p.StartDate:dd MMM} but is still Pending."));
        }

        var notStartedSoon = tasks.Count(t => t.Status == WorkStatus.NotStarted && t.DueDate.HasValue &&
                                              t.DueDate.Value.Date >= today && t.DueDate.Value.Date <= today.AddDays(2));
        if (notStartedSoon > 0)
            alerts.Add(new("Warning", $"{notStartedSoon} task{(notStartedSoon == 1 ? " is" : "s are")} due within 2 days but not started."));

        var blocked = tasks.Count(t => t.IsBlocked && t.Status != WorkStatus.NotStarted);
        if (blocked > 0)
            alerts.Add(new("Info", $"{blocked} task{(blocked == 1 ? " is" : "s are")} in progress while waiting on unfinished dependencies."));

        var people = tasks.Where(t => !t.IsCompleted && !string.IsNullOrWhiteSpace(t.Assignee))
                          .GroupBy(t => t.Assignee).Select(g => (g.Key, n: g.Count())).ToList();
        if (people.Count >= 2)
        {
            var avg = people.Average(x => x.n);
            foreach (var (name, n) in people.Where(x => x.n >= 5 && x.n > avg * 1.5))
                alerts.Add(new("Warning", $"{name} has {n} open tasks (team average {avg:0.#}). Consider reassigning some."));
        }

        var unassigned = tasks.Count(t => !t.IsCompleted && string.IsNullOrWhiteSpace(t.Assignee));
        if (unassigned > 0)
            alerts.Add(new("Info", $"{unassigned} open task{(unassigned == 1 ? " has" : "s have")} no one assigned."));

        if (alerts.Count == 0)
            alerts.Add(new("Success", projects.Count == 0
                ? "Welcome! Create your first project from “My Projects” to get started."
                : "Everything looks on track."));
        return alerts.OrderBy(a => a.Severity switch { "Danger" => 0, "Warning" => 1, "Info" => 2, _ => 3 }).ToList();
    }
}
