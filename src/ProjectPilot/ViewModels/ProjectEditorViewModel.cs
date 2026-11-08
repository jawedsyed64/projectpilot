using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPilot.Data;
using ProjectPilot.Models;
using ProjectPilot.Services;

namespace ProjectPilot.ViewModels;

public partial class ProjectEditorViewModel : ObservableObject
{
    private readonly DataService _data = AppServices.Data;

    public ProjectEditorViewModel(Project? project)
    {
        Model = project ?? new Project
        {
            StartDate = DateTime.Today,
            TargetDate = DateTime.Today.AddMonths(1),
            Owner = _data.CurrentUser,
            Status = ProjectStatus.Pending,
        };
        _name = Model.Name;
        _description = Model.Description;
        _objectives = Model.Objectives;
        _outcomes = Model.Outcomes;
        _startDate = Model.StartDate;
        _targetDate = Model.TargetDate;
        _owner = Model.Owner;
        _teamMembers = Model.TeamMembers;
        _stakeholders = Model.Stakeholders;
        _priority = Model.Priority;
        _status = Model.Status;
        _budgetText = Model.Budget?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        _selectedTemplate = Templates[0];
        LoadSidePanels();
    }

    public Project Model { get; }
    public bool IsNew => Model.Id == 0;
    public bool IsExisting => !IsNew;
    public string WindowTitle => IsNew ? "New Project" : $"Edit Project — {Model.Name}";

    public IReadOnlyList<ProjectTemplate> Templates => ProjectTemplates.All;
    public IReadOnlyList<Priority> Priorities { get; } = Enum.GetValues<Priority>();
    public IReadOnlyList<ProjectStatus> Statuses { get; } = Enum.GetValues<ProjectStatus>();

    [ObservableProperty] private ProjectTemplate _selectedTemplate;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _objectives;
    [ObservableProperty] private string _outcomes;
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _targetDate;
    [ObservableProperty] private string _owner;
    [ObservableProperty] private string _teamMembers;
    [ObservableProperty] private string _stakeholders;
    [ObservableProperty] private Priority _priority;
    [ObservableProperty] private ProjectStatus _status;
    [ObservableProperty] private string _budgetText;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _newComment = "";

    public ObservableCollection<Attachment> Attachments { get; } = new();
    public ObservableCollection<Comment> Comments { get; } = new();
    public ObservableCollection<ActivityEntry> Activity { get; } = new();

    public event Action<bool>? CloseRequested;

    private void LoadSidePanels()
    {
        if (IsNew) return;
        Reset(Attachments, _data.GetAttachments(Model.Id, null));
        Reset(Comments, _data.GetComments(Model.Id, null));
        Reset(Activity, _data.GetActivity(Model.Id, null, 100));
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
        if (string.IsNullOrWhiteSpace(Name)) { Error = "Please enter a project name."; return; }
        if (StartDate.HasValue && TargetDate.HasValue && TargetDate.Value.Date < StartDate.Value.Date)
        { Error = "The target completion date cannot be before the start date."; return; }

        double? budget = null;
        var bt = BudgetText.Trim();
        if (bt.Length > 0)
        {
            if (!double.TryParse(bt, NumberStyles.Number, CultureInfo.CurrentCulture, out var b) &&
                !double.TryParse(bt, NumberStyles.Number, CultureInfo.InvariantCulture, out b))
            { Error = "Budget must be a number, for example 25000."; return; }
            if (b < 0) { Error = "Budget cannot be negative."; return; }
            budget = b;
        }

        bool wasNew = IsNew;
        Model.Name = Name.Trim();
        Model.Description = Description;
        Model.Objectives = Objectives;
        Model.Outcomes = Outcomes;
        Model.StartDate = StartDate;
        Model.TargetDate = TargetDate;
        Model.Owner = Owner;
        Model.TeamMembers = TeamMembers;
        Model.Stakeholders = Stakeholders;
        Model.Priority = Priority;
        Model.Status = Status;
        Model.Budget = budget;

        _data.SaveProject(Model);

        if (wasNew)
        {
            if (SelectedTemplate.Tasks.Count > 0)
                _data.ApplyTemplate(Model.Id, SelectedTemplate, StartDate ?? DateTime.Today, "");
            foreach (var a in Attachments.Where(a => a.IsPending))
            {
                try { _data.AddAttachment(Model.Id, null, a.StoredPath); }
                catch (Exception ex) { Dialogs.Error($"Could not attach {a.FileName}:\n{ex.Message}"); }
            }
        }
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

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
            try { _data.AddAttachment(Model.Id, null, f); }
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
        _data.AddComment(Model.Id, null, NewComment.Trim());
        NewComment = "";
        LoadSidePanels();
    }
}
