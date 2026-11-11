using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPilot.Data;
using ProjectPilot.Models;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public partial class MilestonesViewModel : ViewModelBase
{
    private List<Milestone> _all = new();
    private bool _loading;

    public ObservableCollection<Milestone> Milestones { get; } = new();
    public ObservableCollection<Option> ProjectFilters { get; } = new();

    [ObservableProperty] private Option? _projectFilter;
    [ObservableProperty] private bool _showCompleted = true;
    [ObservableProperty] private string _countText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(ToggleCompleteCommand))]
    private Milestone? _selected;

    partial void OnProjectFilterChanged(Option? value) { if (!_loading) ApplyFilter(); }
    partial void OnShowCompletedChanged(bool value) { if (!_loading) ApplyFilter(); }

    public override void Refresh()
    {
        _loading = true;
        try
        {
            var wanted = ProjectFilter?.Value;
            var selectedId = Selected?.Id;
            ProjectFilters.Clear();
            ProjectFilters.Add(new Option("All projects", null));
            foreach (var p in Data.GetProjects()) ProjectFilters.Add(new Option(p.Name, p.Id));
            ProjectFilter = ProjectFilters.FirstOrDefault(o => Equals(o.Value, wanted)) ?? ProjectFilters[0];
            _all = Data.GetMilestones();
            _loading = false;
            ApplyFilter();
            Selected = Milestones.FirstOrDefault(m => m.Id == selectedId);
        }
        finally { _loading = false; }
    }

    private void ApplyFilter()
    {
        IEnumerable<Milestone> q = _all;
        if (ProjectFilter?.Value is int pid) q = q.Where(m => m.ProjectId == pid);
        if (!ShowCompleted) q = q.Where(m => !m.IsCompleted);
        Milestones.Clear();
        foreach (var m in q.OrderBy(m => m.IsCompleted).ThenBy(m => m.DueDate ?? DateTime.MaxValue)) Milestones.Add(m);
        int overdue = _all.Count(m => m.IsOverdue), soon = _all.Count(m => m.IsDueSoon);
        CountText = $"{Milestones.Count} shown · {overdue} overdue · {soon} due in the next 7 days";
    }

    private bool HasSelection() => Selected != null;

    [RelayCommand]
    private void New()
    {
        if (ProjectFilters.Count <= 1) { Dialogs.Info("Please create a project first (My Projects → New Project)."); return; }
        if (Dialogs.EditMilestone(null, ProjectFilter?.Value as int?)) Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (Selected == null) return;
        var fresh = Data.GetMilestone(Selected.Id);
        if (fresh != null && Dialogs.EditMilestone(fresh, null)) Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleComplete()
    {
        if (Selected == null) return;
        if (!Selected.IsCompleted && Selected.LinkedDone < Selected.LinkedTotal &&
            !Dialogs.Confirm($"{Selected.LinkedTotal - Selected.LinkedDone} linked task(s) are not finished. Mark “{Selected.Title}” completed anyway?"))
            return;
        Data.SetMilestoneCompleted(Selected, !Selected.IsCompleted);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected == null) return;
        if (!Dialogs.Confirm($"Delete milestone “{Selected.Title}”? Its linked tasks are not deleted.", "Delete milestone")) return;
        Data.DeleteMilestone(Selected);
        Refresh();
    }
}

public partial class TaskCheckOption : ObservableObject
{
    public TaskCheckOption(TaskItem t, bool isChecked)
    {
        TaskId = t.Id;
        Title = t.ParentId.HasValue ? "↳ " + t.Title : t.Title;
        Status = t.DisplayStatus;
        Due = t.DueDate;
        _isChecked = isChecked;
    }
    public int TaskId { get; }
    public string Title { get; }
    public WorkStatus Status { get; }
    public DateTime? Due { get; }
    [ObservableProperty] private bool _isChecked;
}

public partial class MilestoneEditorViewModel : ObservableObject
{
    private readonly DataService _data = AppServices.Data;
    private bool _initialising = true;

    public MilestoneEditorViewModel(Milestone? milestone, int? projectId)
    {
        Projects = _data.GetProjects();
        Model = milestone ?? new Milestone
        {
            ProjectId = projectId ?? Projects.FirstOrDefault()?.Id ?? 0,
            DueDate = DateTime.Today.AddDays(14),
        };
        _title = Model.Title;
        _description = Model.Description;
        _dueDate = Model.DueDate;
        _isCompleted = Model.IsCompleted;
        _selectedProject = Projects.FirstOrDefault(p => p.Id == Model.ProjectId) ?? Projects.FirstOrDefault();
        LoadTasks(Model.TaskIds);
        _initialising = false;
    }

    public Milestone Model { get; }
    public bool IsNew => Model.Id == 0;
    public bool CanChangeProject => IsNew;
    public string WindowTitle => IsNew ? "New Milestone" : $"Edit Milestone — {Model.Title}";
    public List<Project> Projects { get; }
    public ObservableCollection<TaskCheckOption> Tasks { get; } = new();
    public bool HasNoTasks => Tasks.Count == 0;

    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private string _error = "";

    public event Action<bool>? CloseRequested;

    partial void OnSelectedProjectChanged(Project? value)
    {
        if (!_initialising) LoadTasks(Array.Empty<int>());
    }

    private void LoadTasks(IEnumerable<int> checkedIds)
    {
        var keep = checkedIds.ToHashSet();
        Tasks.Clear();
        if (SelectedProject != null)
            foreach (var t in _data.GetTasks(SelectedProject.Id).OrderBy(t => t.DueDate ?? DateTime.MaxValue))
                Tasks.Add(new TaskCheckOption(t, keep.Contains(t.Id)));
        OnPropertyChanged(nameof(HasNoTasks));
    }

    [RelayCommand]
    private void UseLatestTaskDate()
    {
        var latest = Tasks.Where(t => t.IsChecked && t.Due.HasValue).Select(t => t.Due!.Value).DefaultIfEmpty().Max();
        if (latest != default) DueDate = latest;
        else Error = "Tick the tasks that belong to this milestone first.";
    }

    [RelayCommand]
    private void Save()
    {
        Error = "";
        if (SelectedProject == null) { Error = "Please create a project first."; return; }
        if (string.IsNullOrWhiteSpace(Title)) { Error = "Please enter a milestone name."; return; }
        if (!DueDate.HasValue) { Error = "Please choose a date for the milestone."; return; }

        Model.ProjectId = SelectedProject.Id;
        Model.Title = Title.Trim();
        Model.Description = Description;
        Model.DueDate = DueDate;
        if (IsCompleted && !Model.IsCompleted) Model.CompletedAt = DateTime.Now;
        if (!IsCompleted) Model.CompletedAt = null;
        Model.TaskIds = Tasks.Where(t => t.IsChecked).Select(t => t.TaskId).ToList();
        _data.SaveMilestone(Model);
        CloseRequested?.Invoke(true);
    }
}
