using Microsoft.Data.Sqlite;
using ProjectPilot.Models;

namespace ProjectPilot.Data;

/// <summary>
/// All reads and writes go through here. Every change is written to SQLite immediately (auto-save)
/// and recorded in the activity log.
/// </summary>
public sealed class DataService
{
    private readonly Database _db;

    public DataService(Database db) => _db = db;

    public string CurrentUser
    {
        get
        {
            var n = GetSetting("user.name");
            return string.IsNullOrWhiteSpace(n) ? Environment.UserName : n;
        }
    }

    // ------------------------------------------------------------------ Projects

    private const string ProjectCols =
        "name,description,objectives,outcomes,start_date,target_date,owner,team_members,stakeholders,priority,budget,status";

    private static Project MapProject(SqliteDataReader r) => new()
    {
        Id = r.Int("id"),
        Name = r.Str("name"),
        Description = r.Str("description"),
        Objectives = r.Str("objectives"),
        Outcomes = r.Str("outcomes"),
        StartDate = r.NDate("start_date"),
        TargetDate = r.NDate("target_date"),
        Owner = r.Str("owner"),
        TeamMembers = r.Str("team_members"),
        Stakeholders = r.Str("stakeholders"),
        Priority = r.EnumOf("priority", Priority.Medium),
        Budget = r.NDouble("budget"),
        Status = r.EnumOf("status", ProjectStatus.Pending),
        CreatedAt = r.Date("created_at"),
        UpdatedAt = r.Date("updated_at"),
    };

    private static (string, object?)[] ProjectParams(Project p) => new (string, object?)[]
    {
        ("$name", p.Name.Trim()), ("$description", p.Description), ("$objectives", p.Objectives),
        ("$outcomes", p.Outcomes), ("$start_date", p.StartDate?.Date), ("$target_date", p.TargetDate?.Date),
        ("$owner", p.Owner.Trim()), ("$team_members", p.TeamMembers.Trim()), ("$stakeholders", p.Stakeholders.Trim()),
        ("$priority", p.Priority), ("$budget", p.Budget), ("$status", p.Status),
        ("$created_at", p.CreatedAt), ("$updated_at", p.UpdatedAt), ("$id", p.Id),
    };

    public List<Project> GetProjects()
    {
        using var c = _db.Open();
        return c.Query("SELECT * FROM projects ORDER BY name COLLATE NOCASE;", MapProject);
    }

    public Project? GetProject(int id)
    {
        using var c = _db.Open();
        return c.Query("SELECT * FROM projects WHERE id = $id;", MapProject, ("$id", id)).FirstOrDefault();
    }

    public int SaveProject(Project p)
    {
        using var c = _db.Open();
        var now = DateTime.Now;
        p.UpdatedAt = now;
        if (p.Id == 0)
        {
            p.CreatedAt = now;
            c.Exec($@"INSERT INTO projects({ProjectCols},created_at,updated_at)
                      VALUES($name,$description,$objectives,$outcomes,$start_date,$target_date,$owner,$team_members,
                             $stakeholders,$priority,$budget,$status,$created_at,$updated_at);", ProjectParams(p));
            p.Id = c.LastId();
            Log(c, p.Id, null, "Project created", p.Name);
        }
        else
        {
            var old = c.Query("SELECT * FROM projects WHERE id = $id;", MapProject, ("$id", p.Id)).FirstOrDefault();
            c.Exec(@"UPDATE projects SET name=$name, description=$description, objectives=$objectives, outcomes=$outcomes,
                        start_date=$start_date, target_date=$target_date, owner=$owner, team_members=$team_members,
                        stakeholders=$stakeholders, priority=$priority, budget=$budget, status=$status, updated_at=$updated_at
                     WHERE id=$id;", ProjectParams(p));
            Log(c, p.Id, null, "Project updated", DescribeProjectChanges(old, p));
        }
        return p.Id;
    }

    private static string DescribeProjectChanges(Project? o, Project n)
    {
        if (o == null) return n.Name;
        var changes = new List<string>();
        if (o.Name != n.Name) changes.Add($"Name: {o.Name} → {n.Name}");
        if (o.Status != n.Status) changes.Add($"Status: {o.Status} → {n.Status}");
        if (o.Priority != n.Priority) changes.Add($"Priority: {o.Priority} → {n.Priority}");
        if (o.Owner != n.Owner) changes.Add($"Owner: {Blank(o.Owner)} → {Blank(n.Owner)}");
        if (o.StartDate?.Date != n.StartDate?.Date) changes.Add($"Start: {D(o.StartDate)} → {D(n.StartDate)}");
        if (o.TargetDate?.Date != n.TargetDate?.Date) changes.Add($"Target: {D(o.TargetDate)} → {D(n.TargetDate)}");
        if (o.Budget != n.Budget) changes.Add($"Budget: {o.Budget?.ToString("N2") ?? "—"} → {n.Budget?.ToString("N2") ?? "—"}");
        return changes.Count == 0 ? "Details edited" : string.Join("; ", changes);
    }

    public void DeleteProject(int id)
    {
        foreach (var a in GetAttachmentsForProjectAll(id)) TryDeleteFile(a.StoredPath);
        using var c = _db.Open();
        c.Exec("DELETE FROM projects WHERE id = $id;", ("$id", id));
    }

    // ------------------------------------------------------------------ Tasks

    private static TaskItem MapTask(SqliteDataReader r) => new()
    {
        Id = r.Int("id"),
        ProjectId = r.Int("project_id"),
        ParentId = r.NInt("parent_id"),
        Title = r.Str("title"),
        Description = r.Str("description"),
        Notes = r.Str("notes"),
        Assignee = r.Str("assignee"),
        Priority = r.EnumOf("priority", Priority.Medium),
        Status = r.EnumOf("status", WorkStatus.NotStarted),
        StartDate = r.NDate("start_date"),
        DueDate = r.NDate("due_date"),
        Progress = r.Int("progress"),
        Recurrence = r.EnumOf("recurrence", Recurrence.None),
        CompletedAt = r.NDate("completed_at"),
        CreatedAt = r.Date("created_at"),
        UpdatedAt = r.Date("updated_at"),
        ProjectName = r.Str("project_name"),
    };

    private const string TaskSelect =
        "SELECT t.*, p.name AS project_name FROM tasks t JOIN projects p ON p.id = t.project_id";

    /// <summary>Loads tasks (optionally for one project) with dependencies, subtask counts and blocked flags.</summary>
    public List<TaskItem> GetTasks(int? projectId = null)
    {
        using var c = _db.Open();
        var tasks = projectId.HasValue
            ? c.Query(TaskSelect + " WHERE t.project_id = $pid ORDER BY t.id;", MapTask, ("$pid", projectId.Value))
            : c.Query(TaskSelect + " ORDER BY t.id;", MapTask);
        Enrich(c, tasks);
        return tasks;
    }

    public TaskItem? GetTask(int id)
    {
        using var c = _db.Open();
        var t = c.Query(TaskSelect + " WHERE t.id = $id;", MapTask, ("$id", id)).FirstOrDefault();
        if (t == null) return null;
        var siblings = c.Query(TaskSelect + " WHERE t.project_id = $pid;", MapTask, ("$pid", t.ProjectId));
        Enrich(c, siblings);
        return siblings.FirstOrDefault(x => x.Id == id);
    }

    private static void Enrich(SqliteConnection c, List<TaskItem> tasks)
    {
        var deps = LoadDependencyGraph(c);
        var all = c.Query("SELECT id, status, parent_id FROM tasks;",
            r => (Id: r.Int("id"), Status: r.EnumOf("status", WorkStatus.NotStarted), Parent: r.NInt("parent_id")));
        var statusById = all.ToDictionary(x => x.Id, x => x.Status);
        var childCounts = all.Where(x => x.Parent.HasValue).GroupBy(x => x.Parent!.Value)
                             .ToDictionary(g => g.Key, g => g.Count());
        foreach (var t in tasks)
        {
            t.DependsOn = deps.TryGetValue(t.Id, out var d) ? d : new List<int>();
            t.SubtaskCount = childCounts.TryGetValue(t.Id, out var n) ? n : 0;
            t.IsBlocked = !t.IsCompleted && t.DependsOn.Any(id =>
                statusById.TryGetValue(id, out var s) && s != WorkStatus.Completed);
        }
    }

    private static Dictionary<int, List<int>> LoadDependencyGraph(SqliteConnection c) =>
        c.Query("SELECT task_id, depends_on_id FROM task_dependencies;",
                r => (Task: r.Int("task_id"), Dep: r.Int("depends_on_id")))
         .GroupBy(x => x.Task)
         .ToDictionary(g => g.Key, g => g.Select(x => x.Dep).ToList());

    /// <summary>True if making <paramref name="taskId"/> depend on <paramref name="dependsOn"/> would create a loop.</summary>
    public bool CreatesCycle(int taskId, IEnumerable<int> dependsOn)
    {
        if (taskId == 0) return false; // a brand-new task cannot be depended upon yet
        using var c = _db.Open();
        var graph = LoadDependencyGraph(c);
        var stack = new Stack<int>(dependsOn);
        var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == taskId) return true;
            if (!seen.Add(n)) continue;
            if (graph.TryGetValue(n, out var next))
                foreach (var x in next) stack.Push(x);
        }
        return false;
    }

    private static (string, object?)[] TaskParams(TaskItem t) => new (string, object?)[]
    {
        ("$project_id", t.ProjectId), ("$parent_id", t.ParentId), ("$title", t.Title.Trim()),
        ("$description", t.Description), ("$notes", t.Notes), ("$assignee", t.Assignee.Trim()),
        ("$priority", t.Priority), ("$status", t.Status), ("$start_date", t.StartDate?.Date),
        ("$due_date", t.DueDate?.Date), ("$progress", Math.Clamp(t.Progress, 0, 100)), ("$recurrence", t.Recurrence),
        ("$completed_at", t.CompletedAt), ("$created_at", t.CreatedAt), ("$updated_at", t.UpdatedAt), ("$id", t.Id),
    };

    /// <summary>
    /// Inserts or updates a task and its dependencies. Handles completion timestamps and
    /// automatically schedules the next occurrence of recurring tasks.
    /// Returns the newly scheduled recurring task, if one was created.
    /// </summary>
    public TaskItem? SaveTask(TaskItem t)
    {
        using var c = _db.Open();
        var now = DateTime.Now;
        TaskItem? old = t.Id == 0 ? null : c.Query(TaskSelect + " WHERE t.id = $id;", MapTask, ("$id", t.Id)).FirstOrDefault();
        bool justCompleted = t.Status == WorkStatus.Completed && old?.Status != WorkStatus.Completed;

        if (t.Status == WorkStatus.Completed)
        {
            t.Progress = 100;
            if (justCompleted || t.CompletedAt == null) t.CompletedAt = now;
        }
        else t.CompletedAt = null;

        TaskItem? spawned = null;
        if (justCompleted && t.Recurrence != Recurrence.None && t.DueDate.HasValue)
        {
            spawned = t.CloneForNextOccurrence();
            spawned.DueDate = Advance(t.DueDate.Value, t.Recurrence);
            if (t.StartDate.HasValue) spawned.StartDate = Advance(t.StartDate.Value, t.Recurrence);
            t.Recurrence = Recurrence.None; // the series continues on the new occurrence
        }

        t.UpdatedAt = now;
        c.Exec("BEGIN;");
        try
        {
            if (t.Id == 0)
            {
                t.CreatedAt = now;
                InsertTask(c, t);
                Log(c, t.ProjectId, t.Id, "Task created", t.Title);
            }
            else
            {
                c.Exec(@"UPDATE tasks SET project_id=$project_id, parent_id=$parent_id, title=$title, description=$description,
                            notes=$notes, assignee=$assignee, priority=$priority, status=$status, start_date=$start_date,
                            due_date=$due_date, progress=$progress, recurrence=$recurrence, completed_at=$completed_at,
                            updated_at=$updated_at
                         WHERE id=$id;", TaskParams(t));
                Log(c, t.ProjectId, t.Id, justCompleted ? "Task completed" : "Task updated",
                    $"{t.Title}: {DescribeTaskChanges(old, t)}");
            }

            c.Exec("DELETE FROM task_dependencies WHERE task_id = $id;", ("$id", t.Id));
            foreach (var dep in t.DependsOn.Distinct().Where(d => d != t.Id))
                c.Exec("INSERT OR IGNORE INTO task_dependencies(task_id, depends_on_id) VALUES($t, $d);", ("$t", t.Id), ("$d", dep));

            if (spawned != null)
            {
                spawned.CreatedAt = now;
                spawned.UpdatedAt = now;
                InsertTask(c, spawned);
                Log(c, spawned.ProjectId, spawned.Id, "Recurring task scheduled",
                    $"{spawned.Title} — next due {D(spawned.DueDate)}");
            }
            c.Exec("COMMIT;");
        }
        catch
        {
            c.Exec("ROLLBACK;");
            throw;
        }
        return spawned;
    }

    private static void InsertTask(SqliteConnection c, TaskItem t)
    {
        c.Exec(@"INSERT INTO tasks(project_id,parent_id,title,description,notes,assignee,priority,status,start_date,due_date,
                                   progress,recurrence,completed_at,created_at,updated_at)
                 VALUES($project_id,$parent_id,$title,$description,$notes,$assignee,$priority,$status,$start_date,$due_date,
                        $progress,$recurrence,$completed_at,$created_at,$updated_at);", TaskParams(t));
        t.Id = c.LastId();
    }

    private static DateTime Advance(DateTime d, Recurrence r) => r switch
    {
        Recurrence.Daily => d.AddDays(1),
        Recurrence.Weekly => d.AddDays(7),
        Recurrence.Monthly => d.AddMonths(1),
        _ => d
    };

    private static string DescribeTaskChanges(TaskItem? o, TaskItem n)
    {
        if (o == null) return "edited";
        var changes = new List<string>();
        if (o.Title != n.Title.Trim()) changes.Add($"renamed from \"{o.Title}\"");
        if (o.Status != n.Status) changes.Add($"status {o.Status} → {n.Status}");
        if (o.Assignee != n.Assignee.Trim()) changes.Add($"assignee {Blank(o.Assignee)} → {Blank(n.Assignee)}");
        if (o.Priority != n.Priority) changes.Add($"priority {o.Priority} → {n.Priority}");
        if (o.DueDate?.Date != n.DueDate?.Date) changes.Add($"due {D(o.DueDate)} → {D(n.DueDate)}");
        if (o.Progress != n.Progress) changes.Add($"progress {o.Progress}% → {n.Progress}%");
        return changes.Count == 0 ? "details edited" : string.Join(", ", changes);
    }

    public void DeleteTask(int id)
    {
        var t = GetTask(id);
        if (t == null) return;
        using var c = _db.Open();
        var files = c.Query("SELECT stored_path FROM attachments WHERE task_id = $id OR task_id IN (SELECT id FROM tasks WHERE parent_id = $id);",
            r => r.Str("stored_path"), ("$id", id));
        c.Exec("DELETE FROM tasks WHERE id = $id;", ("$id", id));
        foreach (var f in files) TryDeleteFile(f);
        Log(c, t.ProjectId, null, "Task deleted", t.Title);
    }

    public List<string> GetKnownPeople()
    {
        using var c = _db.Open();
        var people = c.Query("SELECT DISTINCT assignee AS n FROM tasks WHERE assignee <> '' UNION SELECT DISTINCT owner FROM projects WHERE owner <> '';",
            r => r.Str("n"));
        var teams = c.Query("SELECT team_members FROM projects WHERE team_members <> '';", r => r.Str("team_members"));
        foreach (var t in teams)
            people.AddRange(t.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return people.Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
    }

    // ------------------------------------------------------------------ Templates

    public void ApplyTemplate(int projectId, ProjectTemplate template, DateTime start, string defaultAssignee)
    {
        var ids = new List<int>();
        foreach (var tt in template.Tasks)
        {
            var t = new TaskItem
            {
                ProjectId = projectId,
                Title = tt.Title,
                Priority = tt.Priority,
                StartDate = start.AddDays(tt.StartDay),
                DueDate = start.AddDays(tt.StartDay + Math.Max(1, tt.Days) - 1),
                Assignee = defaultAssignee,
                DependsOn = tt.DependsOn.Where(i => i >= 0 && i < ids.Count).Select(i => ids[i]).ToList(),
            };
            SaveTask(t);
            ids.Add(t.Id);
        }
    }

    // ------------------------------------------------------------------ Attachments

    private static Attachment MapAttachment(SqliteDataReader r) => new()
    {
        Id = r.Int("id"),
        ProjectId = r.NInt("project_id"),
        TaskId = r.NInt("task_id"),
        FileName = r.Str("file_name"),
        StoredPath = r.Str("stored_path"),
        AddedAt = r.Date("added_at"),
    };

    /// <summary>Attachments belonging directly to a project (taskId null) or to a specific task.</summary>
    public List<Attachment> GetAttachments(int projectId, int? taskId)
    {
        using var c = _db.Open();
        return c.Query("SELECT * FROM attachments WHERE project_id = $p AND task_id IS $t ORDER BY added_at DESC;",
            MapAttachment, ("$p", projectId), ("$t", taskId));
    }

    private List<Attachment> GetAttachmentsForProjectAll(int projectId)
    {
        using var c = _db.Open();
        return c.Query("SELECT * FROM attachments WHERE project_id = $p;", MapAttachment, ("$p", projectId));
    }

    /// <summary>Copies the file into the app's attachment store so the project keeps working if the original moves.</summary>
    public void AddAttachment(int projectId, int? taskId, string sourcePath)
    {
        var name = Path.GetFileName(sourcePath);
        var dest = Path.Combine(AppPaths.AttachmentsDir, $"{Guid.NewGuid():N}_{name}");
        File.Copy(sourcePath, dest, overwrite: false);
        using var c = _db.Open();
        c.Exec("INSERT INTO attachments(project_id, task_id, file_name, stored_path, added_at) VALUES($p,$t,$n,$s,$a);",
            ("$p", projectId), ("$t", taskId), ("$n", name), ("$s", dest), ("$a", DateTime.Now));
        Log(c, projectId, taskId, "Attachment added", name);
    }

    public void DeleteAttachment(Attachment a)
    {
        using var c = _db.Open();
        c.Exec("DELETE FROM attachments WHERE id = $id;", ("$id", a.Id));
        TryDeleteFile(a.StoredPath);
        Log(c, a.ProjectId, a.TaskId, "Attachment removed", a.FileName);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (path.StartsWith(AppPaths.AttachmentsDir, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                File.Delete(path);
        }
        catch { /* file in use — leave it */ }
    }

    // ------------------------------------------------------------------ Comments & activity

    public List<Comment> GetComments(int projectId, int? taskId)
    {
        using var c = _db.Open();
        return c.Query("SELECT * FROM comments WHERE project_id = $p AND task_id IS $t ORDER BY created_at DESC, id DESC;",
            r => new Comment
            {
                Id = r.Int("id"),
                ProjectId = r.Int("project_id"),
                TaskId = r.NInt("task_id"),
                Author = r.Str("author"),
                Text = r.Str("text"),
                CreatedAt = r.Date("created_at"),
            }, ("$p", projectId), ("$t", taskId));
    }

    public void AddComment(int projectId, int? taskId, string text)
    {
        using var c = _db.Open();
        c.Exec("INSERT INTO comments(project_id, task_id, author, text, created_at) VALUES($p,$t,$a,$x,$c);",
            ("$p", projectId), ("$t", taskId), ("$a", CurrentUser), ("$x", text), ("$c", DateTime.Now));
        Log(c, projectId, taskId, "Comment added", text.Length > 80 ? text[..80] + "…" : text);
    }

    /// <summary>Activity for a whole project (taskId null) or for one task.</summary>
    public List<ActivityEntry> GetActivity(int? projectId, int? taskId, int limit = 200)
    {
        using var c = _db.Open();
        Func<SqliteDataReader, ActivityEntry> map = r => new ActivityEntry
        {
            Id = r.Int("id"),
            ProjectId = r.NInt("project_id"),
            TaskId = r.NInt("task_id"),
            Action = r.Str("action"),
            Details = r.Str("details"),
            CreatedAt = r.Date("created_at"),
        };
        if (taskId.HasValue)
            return c.Query("SELECT * FROM activity WHERE task_id = $t ORDER BY id DESC LIMIT $l;", map, ("$t", taskId.Value), ("$l", limit));
        if (projectId.HasValue)
            return c.Query("SELECT * FROM activity WHERE project_id = $p ORDER BY id DESC LIMIT $l;", map, ("$p", projectId.Value), ("$l", limit));
        return c.Query("SELECT * FROM activity ORDER BY id DESC LIMIT $l;", map, ("$l", limit));
    }

    private void Log(SqliteConnection c, int? projectId, int? taskId, string action, string details) =>
        c.Exec("INSERT INTO activity(project_id, task_id, action, details, created_at) VALUES($p,$t,$a,$d,$c);",
            ("$p", projectId), ("$t", taskId), ("$a", action), ("$d", details), ("$c", DateTime.Now));

    // ------------------------------------------------------------------ Settings

    public string? GetSetting(string key)
    {
        using var c = _db.Open();
        return c.Scalar("SELECT value FROM settings WHERE key = $k;", ("$k", key)) as string;
    }

    public void SetSetting(string key, string value)
    {
        using var c = _db.Open();
        c.Exec("INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            ("$k", key), ("$v", value));
    }

    // ------------------------------------------------------------------ Backup & restore

    public void BackupTo(string path)
    {
        using var src = _db.Open();
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        dst.Open();
        src.BackupDatabase(dst);
    }

    /// <summary>Daily automatic backup on start-up; keeps the 10 most recent.</summary>
    public void AutoBackup()
    {
        var file = Path.Combine(AppPaths.BackupsDir, $"auto-{DateTime.Today:yyyyMMdd}.db");
        if (!File.Exists(file)) BackupTo(file);
        foreach (var old in new DirectoryInfo(AppPaths.BackupsDir).GetFiles("auto-*.db")
                     .OrderByDescending(f => f.Name).Skip(10))
        {
            try { old.Delete(); } catch { }
        }
    }

    public void RestoreFrom(string path)
    {
        // Validate the file first.
        using (var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        {
            check.Open();
            check.Scalar("SELECT count(*) FROM projects;");
        }
        BackupTo(Path.Combine(AppPaths.BackupsDir, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db"));
        using (var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        using (var dst = _db.Open())
        {
            src.Open();
            src.BackupDatabase(dst);
        }
        SqliteConnection.ClearAllPools();
        _db.Migrate();
    }

    // ------------------------------------------------------------------ helpers

    private static string D(DateTime? d) => d.HasValue ? d.Value.ToString("dd MMM yyyy") : "—";
    private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
}
