namespace ProjectPilot.Models;

public enum Priority { Low, Medium, High, Critical }

public enum ProjectStatus { Pending, Active, OnHold, Completed, Cancelled }

/// <summary>Task status. "Overdue" is assigned automatically when a deadline passes.</summary>
public enum WorkStatus { NotStarted, InProgress, UnderReview, OnHold, Completed, Overdue }

public enum Recurrence { None, Daily, Weekly, Monthly }

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Objectives { get; set; } = "";
    public string Outcomes { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? TargetDate { get; set; }
    public string Owner { get; set; } = "";
    public string TeamMembers { get; set; } = "";
    public string Stakeholders { get; set; } = "";
    public Priority Priority { get; set; } = Priority.Medium;
    public double? Budget { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public IEnumerable<string> TeamList =>
        TeamMembers.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string ToString() => Name;
}

public class TaskItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Assignee { get; set; } = "";
    public Priority Priority { get; set; } = Priority.Medium;
    public WorkStatus Status { get; set; } = WorkStatus.NotStarted;
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public int Progress { get; set; }
    public Recurrence Recurrence { get; set; } = Recurrence.None;
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<int> DependsOn { get; set; } = new();

    // ---- Display / computed (not stored) ----
    public string ProjectName { get; set; } = "";
    public int SubtaskCount { get; set; }
    public bool IsBlocked { get; set; }

    public bool IsCompleted => Status == WorkStatus.Completed;
    public bool IsRecurring => Recurrence != Recurrence.None;
    public bool IsOverdue => !IsCompleted && DueDate.HasValue && DueDate.Value.Date < DateTime.Today;
    public WorkStatus DisplayStatus => IsOverdue ? WorkStatus.Overdue : Status;
    public string AssigneeDisplay => string.IsNullOrWhiteSpace(Assignee) ? "Unassigned" : Assignee;
    public string DisplayTitle => ParentId.HasValue ? "    ↳ " + Title : Title;
    public int EffectiveProgress => IsCompleted ? 100 : Math.Clamp(Progress, 0, 100);

    public string DueGroup
    {
        get
        {
            if (IsCompleted) return "5. Completed";
            if (!DueDate.HasValue) return "4. No due date";
            var d = DueDate.Value.Date;
            if (d < DateTime.Today) return "0. Overdue";
            if (d == DateTime.Today) return "1. Due today";
            if (d <= DateTime.Today.AddDays(7)) return "2. Due this week";
            return "3. Later";
        }
    }

    public TaskItem CloneForNextOccurrence()
    {
        var next = (TaskItem)MemberwiseClone();
        next.Id = 0;
        next.Status = WorkStatus.NotStarted;
        next.Progress = 0;
        next.CompletedAt = null;
        next.DependsOn = new List<int>();
        return next;
    }

    public override string ToString() => Title;
}

public class Attachment
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }
    public int? TaskId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public bool IsPending => Id == 0;
}

public class Comment
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? TaskId { get; set; }
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class ActivityEntry
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }
    public int? TaskId { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Generic label/value pair for combo boxes and filters.</summary>
public sealed record Option(string Label, object? Value)
{
    public override string ToString() => Label;
}
