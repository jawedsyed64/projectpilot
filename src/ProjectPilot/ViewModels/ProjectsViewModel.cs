using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPilot.Converters;
using ProjectPilot.Models;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public sealed class ProjectRow
{
    public ProjectRow(Project p, IReadOnlyCollection<TaskItem> tasks, IReadOnlyCollection<Milestone> milestones)
    {
        Model = p;
        Health = Tracking.Assess(p, tasks, milestones);
        Progress = Metrics.ProjectProgress(p, tasks);
        Total = tasks.Count;
        Done = tasks.Count(t => t.IsCompleted);
        Overdue = tasks.Count(t => t.IsOverdue);
        IsLate = p.Status != ProjectStatus.Completed && p.TargetDate.HasValue && p.TargetDate.Value.Date < DateTime.Today;
    }

    public Project Model { get; }
    public int Id => Model.Id;
    public string Name => Model.Name;
    public ProjectStatus Status => Model.Status;
    public Priority Priority => Model.Priority;
    public string Owner => Model.Owner;
    public DateTime? StartDate => Model.StartDate;
    public DateTime? TargetDate => Model.TargetDate;
    public int Progress { get; }
    public int Total { get; }
    public int Done { get; }
    public int Overdue { get; }
    public bool IsLate { get; }
    public HealthInfo Health { get; }
    public ProjectHealth HealthLevel => Health.Level;
    public string ProgressText => $"{Progress}%";
    public string TaskSummary => Overdue > 0 ? $"{Done}/{Total} · {Overdue} overdue" : $"{Done}/{Total}";
}

public partial class ProjectsViewModel : ViewModelBase
{
    private readonly Action<int> _openTasks;
    private readonly Action<int> _openTimeline;
    private List<ProjectRow> _all = new();

    public ProjectsViewModel(Action<int> openTasks, Action<int> openTimeline)
    {
        _openTasks = openTasks;
        _openTimeline = openTimeline;
        StatusFilters = new List<Option> { new("All statuses", null), new("Open (not completed/cancelled)", "open") };
        StatusFilters.AddRange(Enum.GetValues<ProjectStatus>().Select(s => new Option(EnumDisplayConverter.Humanize(s.ToString()), s)));
        _statusFilter = StatusFilters[0];
    }

    public ObservableCollection<ProjectRow> Projects { get; } = new();
    public List<Option> StatusFilters { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(OpenTasksCommand), nameof(OpenTimelineCommand))]
    private ProjectRow? _selected;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private Option _statusFilter;
    [ObservableProperty] private string _countText = "";

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(Option value) => ApplyFilter();

    public override void Refresh()
    {
        var selectedId = Selected?.Id;
        var tasks = Data.GetTasks();
        var byProject = tasks.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        var msByProject = Data.GetMilestones().GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        _all = Data.GetProjects()
            .Select(p => new ProjectRow(p,
                byProject.TryGetValue(p.Id, out var l) ? l : new List<TaskItem>(),
                msByProject.TryGetValue(p.Id, out var ms) ? ms : new List<Milestone>()))
            .ToList();
        ApplyFilter();
        Selected = Projects.FirstOrDefault(p => p.Id == selectedId);
    }

    private void ApplyFilter()
    {
        IEnumerable<ProjectRow> q = _all;
        var s = Search.Trim();
        if (s.Length > 0)
            q = q.Where(p => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                          || p.Owner.Contains(s, StringComparison.OrdinalIgnoreCase)
                          || p.Model.Description.Contains(s, StringComparison.OrdinalIgnoreCase)
                          || p.Model.TeamMembers.Contains(s, StringComparison.OrdinalIgnoreCase));
        q = StatusFilter?.Value switch
        {
            ProjectStatus st => q.Where(p => p.Status == st),
            "open" => q.Where(p => p.Status is not (ProjectStatus.Completed or ProjectStatus.Cancelled)),
            _ => q,
        };
        Projects.Clear();
        foreach (var p in q) Projects.Add(p);
        CountText = Projects.Count == _all.Count ? $"{_all.Count} projects" : $"Showing {Projects.Count} of {_all.Count} projects";
    }

    [RelayCommand]
    private void New()
    {
        if (Dialogs.EditProject(null)) Refresh();
    }

    private bool HasSelection() => Selected != null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (Selected == null) return;
        var fresh = Data.GetProject(Selected.Id);
        if (fresh != null && Dialogs.EditProject(fresh)) Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected == null) return;
        if (!Dialogs.Confirm($"Delete project “{Selected.Name}”?\n\nAll of its {Selected.Total} tasks, comments and attachments will be permanently deleted.\n(Automatic daily backups are kept in Settings → Backups.)",
                "Delete project")) return;
        Data.DeleteProject(Selected.Id);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenTimeline()
    {
        if (Selected != null) _openTimeline(Selected.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenTasks()
    {
        if (Selected != null) _openTasks(Selected.Id);
    }
}
