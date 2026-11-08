using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPilot.Converters;
using ProjectPilot.Models;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public partial class TasksViewModel : ViewModelBase
{
    private List<TaskItem> _all = new();
    private bool _loading;
    private int? _presetProjectId;

    public TasksViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Tasks);

        StatusFilters = new List<Option> { new("All statuses", null), new("Open (not completed)", "open") };
        StatusFilters.AddRange(Enum.GetValues<WorkStatus>().Select(s => new Option(EnumDisplayConverter.Humanize(s.ToString()), s)));
        PriorityFilters = new List<Option> { new("All priorities", null) };
        PriorityFilters.AddRange(Enum.GetValues<Priority>().Select(p => new Option(p.ToString(), p)));
        GroupOptions = new List<Option>
        {
            new("No grouping", null), new("Project", nameof(TaskItem.ProjectName)), new("Priority", nameof(TaskItem.Priority)),
            new("Owner", nameof(TaskItem.AssigneeDisplay)), new("Status", nameof(TaskItem.DisplayStatus)),
            new("Deadline", nameof(TaskItem.DueGroup)),
        };
        _statusFilter = StatusFilters[1];
        _priorityFilter = PriorityFilters[0];
        _groupBy = GroupOptions[0];
    }

    public ObservableCollection<TaskItem> Tasks { get; } = new();
    public ICollectionView View { get; }

    public ObservableCollection<Option> ProjectFilters { get; } = new();
    public ObservableCollection<Option> AssigneeFilters { get; } = new();
    public List<Option> StatusFilters { get; }
    public List<Option> PriorityFilters { get; }
    public List<Option> GroupOptions { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(MarkCompleteCommand), nameof(AddSubtaskCommand))]
    private TaskItem? _selected;

    [ObservableProperty] private Option? _projectFilter;
    [ObservableProperty] private Option? _assigneeFilter;
    [ObservableProperty] private Option _statusFilter;
    [ObservableProperty] private Option _priorityFilter;
    [ObservableProperty] private Option _groupBy;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _countText = "";

    partial void OnProjectFilterChanged(Option? value) { if (!_loading) ApplyFilter(); }
    partial void OnAssigneeFilterChanged(Option? value) { if (!_loading) ApplyFilter(); }
    partial void OnStatusFilterChanged(Option value) { if (!_loading) ApplyFilter(); }
    partial void OnPriorityFilterChanged(Option value) { if (!_loading) ApplyFilter(); }
    partial void OnSearchChanged(string value) { if (!_loading) ApplyFilter(); }

    partial void OnGroupByChanged(Option value)
    {
        View.GroupDescriptions.Clear();
        if (value?.Value is string prop)
        {
            View.GroupDescriptions.Add(new PropertyGroupDescription(prop));
            if (prop == nameof(TaskItem.DueGroup))
            {
                View.SortDescriptions.Clear();
                View.SortDescriptions.Add(new SortDescription(nameof(TaskItem.DueGroup), ListSortDirection.Ascending));
            }
        }
    }

    /// <summary>Called from "My Projects → Open tasks" so the list opens filtered on that project.</summary>
    public void PresetProject(int projectId) => _presetProjectId = projectId;

    public override void Refresh()
    {
        _loading = true;
        try
        {
            var selectedId = Selected?.Id;
            var wantedProject = _presetProjectId.HasValue ? (object)_presetProjectId.Value : ProjectFilter?.Value;
            var wantedAssignee = AssigneeFilter?.Value;
            _presetProjectId = null;

            ProjectFilters.Clear();
            ProjectFilters.Add(new Option("All projects", null));
            foreach (var p in Data.GetProjects()) ProjectFilters.Add(new Option(p.Name, p.Id));
            ProjectFilter = ProjectFilters.FirstOrDefault(o => Equals(o.Value, wantedProject)) ?? ProjectFilters[0];

            _all = Data.GetTasks();

            AssigneeFilters.Clear();
            AssigneeFilters.Add(new Option("Everyone", null));
            foreach (var a in _all.Select(t => t.AssigneeDisplay).Distinct().OrderBy(a => a == "Unassigned").ThenBy(a => a))
                AssigneeFilters.Add(new Option(a, a));
            AssigneeFilter = AssigneeFilters.FirstOrDefault(o => Equals(o.Value, wantedAssignee)) ?? AssigneeFilters[0];

            _loading = false;
            ApplyFilter();
            Selected = Tasks.FirstOrDefault(t => t.Id == selectedId);
        }
        finally { _loading = false; }
    }

    private void ApplyFilter()
    {
        IEnumerable<TaskItem> q = _all;
        if (ProjectFilter?.Value is int pid) q = q.Where(t => t.ProjectId == pid);
        if (AssigneeFilter?.Value is string who) q = q.Where(t => t.AssigneeDisplay == who);
        q = StatusFilter?.Value switch
        {
            WorkStatus s => q.Where(t => t.DisplayStatus == s),
            "open" => q.Where(t => !t.IsCompleted),
            _ => q,
        };
        if (PriorityFilter?.Value is Priority pr) q = q.Where(t => t.Priority == pr);
        var s2 = Search.Trim();
        if (s2.Length > 0)
            q = q.Where(t => t.Title.Contains(s2, StringComparison.OrdinalIgnoreCase)
                          || t.Description.Contains(s2, StringComparison.OrdinalIgnoreCase)
                          || t.Notes.Contains(s2, StringComparison.OrdinalIgnoreCase)
                          || t.Assignee.Contains(s2, StringComparison.OrdinalIgnoreCase));

        // Keep subtasks directly under their parent when they're both visible.
        var list = q.ToList();
        var ids = list.Select(t => t.Id).ToHashSet();
        var ordered = new List<TaskItem>();
        foreach (var t in list.Where(t => t.ParentId == null || !ids.Contains(t.ParentId.Value))
                              .OrderBy(t => t.ProjectName).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.Id))
        {
            ordered.Add(t);
            ordered.AddRange(list.Where(c => c.ParentId == t.Id).OrderBy(c => c.DueDate ?? DateTime.MaxValue).ThenBy(c => c.Id));
        }

        Tasks.Clear();
        foreach (var t in ordered) Tasks.Add(t);
        CountText = $"{Tasks.Count} task{(Tasks.Count == 1 ? "" : "s")} shown · {_all.Count(t => t.IsOverdue)} overdue in total";
    }

    [RelayCommand]
    private void NewTask()
    {
        if (ProjectFilters.Count <= 1)
        {
            Dialogs.Info("Please create a project first (My Projects → New Project).");
            return;
        }
        var pid = ProjectFilter?.Value as int?;
        if (Dialogs.EditTask(null, pid, null)) Refresh();
    }

    private bool HasSelection() => Selected != null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddSubtask()
    {
        if (Selected == null) return;
        var parentId = Selected.ParentId ?? Selected.Id;
        if (Dialogs.EditTask(null, Selected.ProjectId, parentId)) Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (Selected == null) return;
        var fresh = Data.GetTask(Selected.Id);
        if (fresh != null && Dialogs.EditTask(fresh)) Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MarkComplete()
    {
        if (Selected == null) return;
        var t = Data.GetTask(Selected.Id);
        if (t == null || t.IsCompleted) return;

        var unfinishedDeps = _all.Where(x => t.DependsOn.Contains(x.Id) && !x.IsCompleted).Select(x => x.Title).ToList();
        if (unfinishedDeps.Count > 0 && !Dialogs.Confirm(
                $"“{t.Title}” depends on tasks that are not finished:\n\n• {string.Join("\n• ", unfinishedDeps)}\n\nMark it completed anyway?"))
            return;
        var openSubs = _all.Count(x => x.ParentId == t.Id && !x.IsCompleted);
        if (openSubs > 0 && !Dialogs.Confirm($"{openSubs} subtask(s) of “{t.Title}” are still open. Mark it completed anyway?"))
            return;

        t.Status = WorkStatus.Completed;
        var next = Data.SaveTask(t);
        Refresh();
        if (next != null)
            Dialogs.Info($"This is a recurring task. The next one has been scheduled for {next.DueDate:dddd, d MMMM yyyy}.");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected == null) return;
        var extra = Selected.SubtaskCount > 0 ? $"\n\nIts {Selected.SubtaskCount} subtask(s) will also be deleted." : "";
        if (!Dialogs.Confirm($"Delete task “{Selected.Title}”?{extra}", "Delete task")) return;
        Data.DeleteTask(Selected.Id);
        Refresh();
    }
}
