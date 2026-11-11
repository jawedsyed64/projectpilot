using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPilot.Controls;
using ProjectPilot.Converters;
using ProjectPilot.Models;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public partial class TimelineViewModel : ViewModelBase
{
    private List<TaskItem> _tasks = new();
    private List<Milestone> _milestones = new();
    private Project? _project;
    private bool _loading;

    public TimelineViewModel()
    {
        ZoomOptions = new List<Option> { new("Days", 30.0), new("Weeks", 12.0), new("Months", 4.0) };
        _zoom = ZoomOptions[0];
    }

    public ObservableCollection<Option> ProjectOptions { get; } = new();
    public List<Option> ZoomOptions { get; }

    [ObservableProperty] private Option? _selectedProject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixelsPerDay))]
    private Option _zoom;

    [ObservableProperty] private bool _showCritical = true;
    [ObservableProperty] private bool _showBaseline;
    [ObservableProperty] private GanttData? _chart;
    [ObservableProperty] private bool _hasProject;

    // ---- tracking summary ----
    [ObservableProperty] private ProjectHealth _healthLevel;
    [ObservableProperty] private string _healthReason = "";
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _expectedText = "";
    [ObservableProperty] private string _milestoneText = "";
    [ObservableProperty] private string _plannedEndText = "";
    [ObservableProperty] private string _forecastEndText = "";
    [ObservableProperty] private string _varianceText = "";
    [ObservableProperty] private string _varianceKey = "Neutral";
    [ObservableProperty] private int _overdueCount;
    [ObservableProperty] private int _reviewCount;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private List<TaskItem> _attention = new();

    public double PixelsPerDay => Zoom.Value is double d ? d : 30.0;

    partial void OnSelectedProjectChanged(Option? value) { if (!_loading) Load(); }

    /// <summary>Lets other pages open the timeline on a specific project.</summary>
    public void PresetProject(int projectId) => _presetProjectId = projectId;
    private int? _presetProjectId;

    public override void Refresh()
    {
        _loading = true;
        try
        {
            var wanted = _presetProjectId.HasValue ? (object)_presetProjectId.Value : SelectedProject?.Value;
            _presetProjectId = null;
            var projects = Data.GetProjects();
            ProjectOptions.Clear();
            foreach (var p in projects.OrderBy(p => p.Status is ProjectStatus.Completed or ProjectStatus.Cancelled).ThenBy(p => p.Name))
                ProjectOptions.Add(new Option(p.Name, p.Id));
            SelectedProject = ProjectOptions.FirstOrDefault(o => Equals(o.Value, wanted)) ?? ProjectOptions.FirstOrDefault();
        }
        finally { _loading = false; }
        Load();
    }

    private void Load()
    {
        if (SelectedProject?.Value is not int pid || (_project = Data.GetProject(pid)) == null)
        {
            HasProject = false;
            Chart = null;
            return;
        }
        HasProject = true;
        _tasks = Data.GetTasks(pid);
        _milestones = Data.GetMilestones(pid);
        var critical = Scheduler.CriticalPath(_tasks);

        // ---- rows: milestones first, then tasks with their subtasks ----
        var rows = new List<GanttRow>();
        foreach (var m in _milestones)
        {
            rows.Add(new GanttRow
            {
                Kind = GanttRowKind.Milestone,
                Id = m.Id,
                Label = m.Title,
                SubLabel = $"{m.StatusText} · {m.LinkedText}",
                Start = m.DueDate,
                End = m.DueDate,
                IsCompleted = m.IsCompleted,
                IsOverdue = m.IsOverdue,
                Tooltip = $"Milestone: {m.Title}\nDue: {Fmt(m.DueDate)}\n{m.StatusText} · {m.LinkedText}\n\nDrag the diamond to change the date · double-click to edit",
            });
        }
        var top = _tasks.Where(t => t.ParentId == null)
                        .OrderBy(t => t.StartDate ?? t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.Id);
        foreach (var t in top)
        {
            rows.Add(MakeRow(t, 0, critical));
            foreach (var c in _tasks.Where(x => x.ParentId == t.Id).OrderBy(x => x.StartDate ?? x.DueDate ?? DateTime.MaxValue).ThenBy(x => x.Id))
                rows.Add(MakeRow(c, 1, critical));
        }

        // ---- date range ----
        var dates = new List<DateTime> { DateTime.Today };
        if (_project.StartDate.HasValue) dates.Add(_project.StartDate.Value.Date);
        if (_project.TargetDate.HasValue) dates.Add(_project.TargetDate.Value.Date);
        foreach (var r in rows)
        {
            if (r.Start.HasValue) dates.Add(r.Start.Value.Date);
            if (r.End.HasValue) dates.Add(r.End.Value.Date);
            if (r.BaselineStart.HasValue) dates.Add(r.BaselineStart.Value.Date);
            if (r.BaselineEnd.HasValue) dates.Add(r.BaselineEnd.Value.Date);
        }
        var min = dates.Min().AddDays(-7);
        var max = dates.Max().AddDays(21);
        // start on a Monday so weeks line up
        while (min.DayOfWeek != DayOfWeek.Monday) min = min.AddDays(-1);

        Chart = new GanttData { Rows = rows, RangeStart = min, RangeEnd = max, Target = _project.TargetDate };

        // ---- tracking ----
        var health = Tracking.Assess(_project, _tasks, _milestones);
        HealthLevel = health.Level;
        HealthReason = health.Reason;
        Progress = health.Progress;
        ExpectedText = health.ExpectedProgress.HasValue ? $"{health.ExpectedProgress}% of the planned time used" : "No start/target dates";
        MilestoneText = _milestones.Count == 0 ? "None yet" : $"{_milestones.Count(m => m.IsCompleted)} of {_milestones.Count} completed";
        PlannedEndText = Fmt(_project.TargetDate);
        ForecastEndText = health.ForecastEnd.HasValue ? Fmt(health.ForecastEnd) : "—";
        if (!_project.TargetDate.HasValue || !health.ForecastEnd.HasValue) { VarianceText = "—"; VarianceKey = "Neutral"; }
        else if (health.VarianceDays > 0) { VarianceText = $"{health.VarianceDays} day{(health.VarianceDays == 1 ? "" : "s")} late"; VarianceKey = "Danger"; }
        else if (health.VarianceDays < 0) { VarianceText = $"{-health.VarianceDays} day{(health.VarianceDays == -1 ? "" : "s")} early"; VarianceKey = "Success"; }
        else { VarianceText = "On the target date"; VarianceKey = "Success"; }
        OverdueCount = _tasks.Count(t => t.IsOverdue);
        ReviewCount = _tasks.Count(t => t.Status == WorkStatus.UnderReview);
        CriticalCount = critical.Count;

        // Delayed / slipped / awaiting review items
        Attention = _tasks.Where(t => t.IsOverdue || t.Status == WorkStatus.UnderReview || (!t.IsCompleted && t.SlipDays > 0))
                          .OrderByDescending(t => t.IsOverdue).ThenBy(t => t.DueDate).Take(12).ToList();
    }

    private static GanttRow MakeRow(TaskItem t, int indent, HashSet<int> critical)
    {
        var slip = t.SlipDays;
        var sub = $"{t.AssigneeDisplay} · {EnumDisplayConverter.Humanize(t.DisplayStatus.ToString())}";
        if (!t.StartDate.HasValue && !t.DueDate.HasValue) sub += " · no dates";
        else if (slip > 0 && !t.IsCompleted) sub += $" · +{slip}d vs plan";
        return new GanttRow
        {
            Kind = GanttRowKind.Task,
            Id = t.Id,
            Label = t.Title,
            SubLabel = sub,
            Indent = indent,
            Start = t.StartDate,
            End = t.DueDate,
            BaselineStart = t.BaselineStart,
            BaselineEnd = t.BaselineDue,
            Progress = t.EffectiveProgress,
            Status = t.Status,
            IsCompleted = t.IsCompleted,
            IsOverdue = t.IsOverdue,
            IsCritical = critical.Contains(t.Id),
            IsSummary = t.SubtaskCount > 0,
            DependsOn = t.DependsOn,
            Tooltip = $"{t.Title}\n{Fmt(t.StartDate)} – {Fmt(t.DueDate)}" +
                      (t.StartDate.HasValue && t.DueDate.HasValue ? $"  ({Scheduler.Duration(t.StartDate, t.DueDate)} days)" : "") +
                      $"\nPlanned: {Fmt(t.BaselineStart)} – {Fmt(t.BaselineDue)}" +
                      (t.StartedAt.HasValue ? $"\nActually started: {Fmt(t.StartedAt)}" : "") +
                      (t.CompletedAt.HasValue ? $"\nCompleted: {Fmt(t.CompletedAt)}" : "") +
                      $"\nOwner: {t.AssigneeDisplay} · {t.EffectiveProgress}% · {EnumDisplayConverter.Humanize(t.DisplayStatus.ToString())}" +
                      (critical.Contains(t.Id) ? "\nOn the critical path — any delay moves the project end." : "") +
                      "\n\nDrag to move · drag the right edge to change the deadline · double-click to edit",
        };
    }

    private static string Fmt(DateTime? d) => d.HasValue ? d.Value.ToString("dd MMM yyyy") : "—";

    // ---------------------------------------------------------------- actions from the chart

    public void MoveRow(GanttRowEventArgs e)
    {
        if (e.Kind == GanttRowKind.Milestone)
        {
            var m = _milestones.FirstOrDefault(x => x.Id == e.Id);
            if (m == null) return;
            Data.MoveMilestone(m, e.NewEnd);
            Load();
            return;
        }

        var t = _tasks.FirstOrDefault(x => x.Id == e.Id);
        if (t == null) return;
        // A task with only a due date keeps having only a due date.
        DateTime? newStart = t.StartDate.HasValue ? e.NewStart : null;
        DateTime? newDue = t.DueDate.HasValue ? e.NewEnd : null;
        if (!t.DueDate.HasValue && t.StartDate.HasValue) newStart = e.NewStart;

        var plan = Scheduler.PlanMove(_tasks, t.Id, newStart, newDue);
        if (plan.Count == 0) return;
        var knockOn = plan.Where(c => c.Task.Id != t.Id).ToList();
        if (knockOn.Count > 0)
        {
            var list = string.Join("\n", knockOn.Take(12).Select(c => "• " + c.Describe()));
            if (knockOn.Count > 12) list += $"\n… and {knockOn.Count - 12} more";
            if (!Dialogs.Confirm(
                    $"Moving “{t.Title}” means these dependent tasks would start before it finishes, so they will move too:\n\n{list}\n\nApply these changes?",
                    "Update dependent tasks"))
            {
                Load();
                return;
            }
        }
        Data.RescheduleTasks(plan, "moved on the timeline");
        Load();
    }

    public void OpenRow(GanttRowEventArgs e)
    {
        if (e.Kind == GanttRowKind.Milestone)
        {
            var m = Data.GetMilestone(e.Id);
            if (m != null && Dialogs.EditMilestone(m, null)) Load();
            return;
        }
        var t = Data.GetTask(e.Id);
        if (t != null && Dialogs.EditTask(t)) Load();
    }

    // ---------------------------------------------------------------- commands

    [RelayCommand]
    private void Reload() => Refresh();

    [RelayCommand]
    private void SetBaseline()
    {
        if (_project == null) return;
        if (!Dialogs.Confirm(
                $"Save the current dates of all tasks in “{_project.Name}” as the planned schedule (baseline)?\n\n" +
                "“Planned vs actual” comparisons will then be measured against today's dates.", "Set baseline")) return;
        Data.SetBaseline(_project.Id);
        ShowBaseline = true;
        Load();
    }

    [RelayCommand]
    private void AddMilestone()
    {
        if (_project == null) { Dialogs.Info("Please create a project first."); return; }
        if (Dialogs.EditMilestone(null, _project.Id)) Load();
    }

    [RelayCommand]
    private void AddTask()
    {
        if (_project == null) { Dialogs.Info("Please create a project first."); return; }
        if (Dialogs.EditTask(null, _project.Id, null)) Load();
    }

    [RelayCommand]
    private void OpenAttention(TaskItem? t)
    {
        if (t == null) return;
        var fresh = Data.GetTask(t.Id);
        if (fresh != null && Dialogs.EditTask(fresh)) Load();
    }
}
