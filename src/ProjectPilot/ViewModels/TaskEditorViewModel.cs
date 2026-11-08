using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPilot.Converters;
using ProjectPilot.Data;
using ProjectPilot.Models;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public partial class DependencyOption : ObservableObject
{
    public DependencyOption(TaskItem t, bool isChecked)
    {
        TaskId = t.Id;
        Title = t.Title;
        Status = t.DisplayStatus;
        _isChecked = isChecked;
    }

    public int TaskId { get; }
    public string Title { get; }
    public WorkStatus Status { get; }
    public string StatusText => EnumDisplayConverter.Humanize(Status.ToString());
    [ObservableProperty] private bool _isChecked;
}

public partial class TaskEditorViewModel : ObservableObject
{
    private readonly DataService _data = AppServices.Data;
    private List<TaskItem> _projectTasks = new();
    private bool _initialising = true;

    public TaskEditorViewModel(TaskItem? task, int? projectId, int? parentId)
    {
        Projects = _data.GetProjects();
        Model = task ?? new TaskItem
        {
            ProjectId = projectId ?? Projects.FirstOrDefault()?.Id ?? 0,
            ParentId = parentId,
            StartDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(7),
        };
        CanChangeProject = Model.Id == 0 && parentId == null;

        _title = Model.Title;
        _description = Model.Description;
        _notes = Model.Notes;
        _assignee = Model.Assignee;
        _priority = Model.Priority;
        _status = Model.Status == WorkStatus.Overdue ? WorkStatus.InProgress : Model.Status;
        _startDate = Model.StartDate;
        _dueDate = Model.DueDate;
        _progress = Model.Progress;
        _recurrence = Model.Recurrence;
        _selectedProject = Projects.FirstOrDefault(p => p.Id == Model.ProjectId) ?? Projects.FirstOrDefault();

        LoadProjectContext(Model.ParentId, Model.DependsOn);
        LoadSidePanels();
        _initialising = false;
    }

    public TaskItem Model { get; }
    public bool IsNew => Model.Id == 0;
    public bool IsExisting => !IsNew;
    public bool CanChangeProject { get; }
    public string WindowTitle => IsNew ? (Model.ParentId.HasValue ? "New Subtask" : "New Task") : $"Edit Task — {Model.Title}";

    public List<Project> Projects { get; }
    public IReadOnlyList<Priority> Priorities { get; } = Enum.GetValues<Priority>();
    public IReadOnlyList<WorkStatus> Statuses { get; } = Enum.GetValues<WorkStatus>().Where(s => s != WorkStatus.Overdue).ToList();
    public IReadOnlyList<Recurrence> Recurrences { get; } = Enum.GetValues<Recurrence>();

    public ObservableCollection<Option> ParentOptions { get; } = new();
    public ObservableCollection<string> PeopleSuggestions { get; } = new();
    public ObservableCollection<DependencyOption> Dependencies { get; } = new();
    public ObservableCollection<TaskItem> Subtasks { get; } = new();
    public ObservableCollection<Attachment> Attachments { get; } = new();
    public ObservableCollection<Comment> Comments { get; } = new();
    public ObservableCollection<ActivityEntry> Activity { get; } = new();

    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private Option? _selectedParent;
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private string _assignee;
    [ObservableProperty] private Priority _priority;
    [ObservableProperty] private WorkStatus _status;
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private Recurrence _recurrence;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _newComment = "";

    public bool HasNoDependencyOptions => Dependencies.Count == 0;
    public string OverdueNote => Model.IsOverdue ? $"This task is overdue (was due {Model.DueDate:dd MMM yyyy})." : "";

    public event Action<bool>? CloseRequested;

    partial void OnSelectedProjectChanged(Project? value)
    {
        if (_initialising) return;
        var keepDeps = Dependencies.Where(d => d.IsChecked).Select(d => d.TaskId).ToList();
        LoadProjectContext(SelectedParent?.Value as int?, keepDeps);
    }

    partial void OnStatusChanged(WorkStatus value)
    {
        if (_initialising) return;
        if (value == WorkStatus.Completed) Progress = 100;
        else if (value == WorkStatus.NotStarted && Progress == 100) Progress = 0;
    }

    partial void OnProgressChanged(int value)
    {
        if (_initialising) return;
        if (value > 0 && value < 100 && Status == WorkStatus.NotStarted) Status = WorkStatus.InProgress;
    }

    private void LoadProjectContext(int? wantedParent, IEnumerable<int> checkedDeps)
    {
        _projectTasks = SelectedProject == null ? new List<TaskItem>() : _data.GetTasks(SelectedProject.Id);
        var hasChildren = Model.Id != 0 && _projectTasks.Any(t => t.ParentId == Model.Id);

        ParentOptions.Clear();
        ParentOptions.Add(new Option("(none — top-level task)", null));
        if (!hasChildren)
            foreach (var t in _projectTasks.Where(t => t.ParentId == null && t.Id != Model.Id))
                ParentOptions.Add(new Option(t.Title, t.Id));
        SelectedParent = ParentOptions.FirstOrDefault(o => o.Value is int id && id == wantedParent) ?? ParentOptions[0];

        var keep = checkedDeps.ToHashSet();
        Dependencies.Clear();
        foreach (var t in _projectTasks.Where(t => t.Id != Model.Id && (Model.Id == 0 || t.ParentId != Model.Id)))
            Dependencies.Add(new DependencyOption(t, keep.Contains(t.Id)));
        OnPropertyChanged(nameof(HasNoDependencyOptions));

        PeopleSuggestions.Clear();
        var people = _data.GetKnownPeople();
        if (SelectedProject != null) people.AddRange(SelectedProject.TeamList);
        foreach (var p in people.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p)) PeopleSuggestions.Add(p);
    }

    private void LoadSidePanels()
    {
        if (IsNew) return;
        Reset(Subtasks, _projectTasks.Where(t => t.ParentId == Model.Id));
        Reset(Attachments, _data.GetAttachments(Model.ProjectId, Model.Id));
        Reset(Comments, _data.GetComments(Model.ProjectId, Model.Id));
        Reset(Activity, _data.GetActivity(null, Model.Id, 100));
    }

    private static void Reset<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    [RelayCommand]
    private void Save()
    {
        Error = "";
        if (SelectedProject == null) { Error = "Please create a project first."; return; }
        if (string.IsNullOrWhiteSpace(Title)) { Error = "Please enter a task title."; return; }
        if (StartDate.HasValue && DueDate.HasValue && DueDate.Value.Date < StartDate.Value.Date)
        { Error = "The due date cannot be before the start date."; return; }

        var deps = Dependencies.Where(d => d.IsChecked).Select(d => d.TaskId).ToList();
        if (_data.CreatesCycle(Model.Id, deps))
        { Error = "Those dependencies would create a loop (a task would end up waiting for itself). Please untick one."; return; }

        if (Status is WorkStatus.InProgress or WorkStatus.UnderReview or WorkStatus.Completed)
        {
            var unfinished = Dependencies.Where(d => d.IsChecked && d.Status != WorkStatus.Completed).Select(d => d.Title).ToList();
            if (unfinished.Count > 0 && !Dialogs.Confirm(
                    $"This task depends on tasks that are not finished yet:\n\n• {string.Join("\n• ", unfinished)}\n\nSave with status “{EnumDisplayConverter.Humanize(Status.ToString())}” anyway?"))
                return;
        }
        if (Status == WorkStatus.Completed && Subtasks.Any(s => !s.IsCompleted) &&
            !Dialogs.Confirm("Some subtasks are not completed yet. Mark this task completed anyway?"))
            return;
        if (Recurrence != Recurrence.None && !DueDate.HasValue)
        { Error = "A recurring task needs a due date so the next occurrence can be scheduled."; return; }

        bool wasNew = IsNew;
        Model.ProjectId = SelectedProject.Id;
        Model.ParentId = SelectedParent?.Value as int?;
        Model.Title = Title.Trim();
        Model.Description = Description;
        Model.Notes = Notes;
        Model.Assignee = (Assignee ?? "").Trim();
        Model.Priority = Priority;
        Model.Status = Status;
        Model.StartDate = StartDate;
        Model.DueDate = DueDate;
        Model.Progress = Math.Clamp(Progress, 0, 100);
        Model.Recurrence = Recurrence;
        Model.DependsOn = deps;

        var next = _data.SaveTask(Model);

        if (wasNew)
        {
            foreach (var a in Attachments.Where(a => a.IsPending))
            {
                try { _data.AddAttachment(Model.ProjectId, Model.Id, a.StoredPath); }
                catch (Exception ex) { Dialogs.Error($"Could not attach {a.FileName}:\n{ex.Message}"); }
            }
        }
        if (next != null)
            Dialogs.Info($"This is a recurring task. The next one has been scheduled for {next.DueDate:dddd, d MMMM yyyy}.");
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private void AddSubtask()
    {
        if (IsNew) { Dialogs.Info("Save this task first, then you can add subtasks to it."); return; }
        if (Model.ParentId.HasValue) { Dialogs.Info("Subtasks can only be added to top-level tasks."); return; }
        if (Dialogs.EditTask(null, Model.ProjectId, Model.Id))
        {
            _projectTasks = _data.GetTasks(Model.ProjectId);
            LoadSidePanels();
        }
    }

    [RelayCommand]
    private void AddAttachment()
    {
        foreach (var f in Dialogs.PickFiles())
        {
            if (IsNew)
            {
                Attachments.Add(new Attachment { FileName = Path.GetFileName(f), StoredPath = f, AddedAt = DateTime.Now });
                continue;
            }
            try { _data.AddAttachment(Model.ProjectId, Model.Id, f); }
            catch (Exception ex) { Dialogs.Error($"Could not attach {Path.GetFileName(f)}:\n{ex.Message}"); }
        }
        LoadSidePanels();
    }

    [RelayCommand]
    private void OpenAttachment(Attachment? a)
    {
        if (a != null) Dialogs.OpenFile(a.StoredPath);
    }

    [RelayCommand]
    private void RemoveAttachment(Attachment? a)
    {
        if (a == null) return;
        if (a.IsPending) { Attachments.Remove(a); return; }
        if (!Dialogs.Confirm($"Remove attachment “{a.FileName}”?")) return;
        _data.DeleteAttachment(a);
        LoadSidePanels();
    }

    [RelayCommand]
    private void AddComment()
    {
        if (IsNew || string.IsNullOrWhiteSpace(NewComment)) return;
        _data.AddComment(Model.ProjectId, Model.Id, NewComment.Trim());
        NewComment = "";
        LoadSidePanels();
    }
}
