using ProjectPilot.Models;

namespace ProjectPilot.Services;

/// <summary>A proposed change to a task's dates.</summary>
public sealed record DateChange(TaskItem Task, DateTime? OldStart, DateTime? OldDue, DateTime? NewStart, DateTime? NewDue)
{
    public string Describe() =>
        $"{Task.Title}: {Fmt(OldStart)}–{Fmt(OldDue)}  →  {Fmt(NewStart)}–{Fmt(NewDue)}";

    private static string Fmt(DateTime? d) => d.HasValue ? d.Value.ToString("dd MMM") : "—";
}

/// <summary>Schedule calculations: dependency shifting (finish-to-start) and critical path.</summary>
public static class Scheduler
{
    public static int Duration(DateTime? start, DateTime? due) =>
        start.HasValue && due.HasValue ? Math.Max(1, (due.Value.Date - start.Value.Date).Days + 1) : 1;

    /// <summary>
    /// Works out what happens if <paramref name="taskId"/> gets new dates: every unfinished task that depends on it
    /// (directly or indirectly) and would now start before its predecessor finishes is pushed later, keeping its duration.
    /// Tasks are never pulled earlier automatically. Returns only tasks whose dates actually change.
    /// </summary>
    public static List<DateChange> PlanMove(IReadOnlyList<TaskItem> projectTasks, int taskId, DateTime? newStart, DateTime? newDue)
    {
        var byId = projectTasks.ToDictionary(t => t.Id);
        var successors = projectTasks
            .SelectMany(t => t.DependsOn.Select(d => (Pred: d, Succ: t.Id)))
            .GroupBy(x => x.Pred)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Succ).Distinct().ToList());

        var start = projectTasks.ToDictionary(t => t.Id, t => t.StartDate?.Date);
        var due = projectTasks.ToDictionary(t => t.Id, t => t.DueDate?.Date);
        start[taskId] = newStart?.Date;
        due[taskId] = newDue?.Date;

        var queue = new Queue<int>();
        queue.Enqueue(taskId);
        int guard = 0;
        while (queue.Count > 0 && guard++ < 100_000)
        {
            var p = queue.Dequeue();
            if (!successors.TryGetValue(p, out var succs) || !due.TryGetValue(p, out var pDue) || !pDue.HasValue) continue;
            var earliest = pDue.Value.AddDays(1);
            foreach (var s in succs)
            {
                if (!byId.TryGetValue(s, out var st) || st.IsCompleted) continue;
                var sStart = start[s];
                var sDue = due[s];
                if (sStart.HasValue)
                {
                    if (sStart.Value >= earliest) continue;
                    var dur = Duration(sStart, sDue);
                    start[s] = earliest;
                    due[s] = sDue.HasValue ? earliest.AddDays(dur - 1) : null;
                }
                else if (sDue.HasValue)
                {
                    if (sDue.Value >= earliest) continue;
                    due[s] = earliest;
                }
                else continue;
                queue.Enqueue(s);
            }
        }

        var changes = new List<DateChange>();
        foreach (var t in projectTasks)
        {
            var ns = start[t.Id];
            var nd = due[t.Id];
            if (ns != t.StartDate?.Date || nd != t.DueDate?.Date)
                changes.Add(new DateChange(t, t.StartDate, t.DueDate, ns, nd));
        }
        // the task the user moved first, then the knock-on changes
        return changes.OrderBy(c => c.Task.Id == taskId ? 0 : 1).ThenBy(c => c.NewStart ?? c.NewDue).ToList();
    }

    /// <summary>
    /// Critical path on the scheduled dates: unfinished tasks with no slack, i.e. any delay to them
    /// delays the end of the project. Summary tasks (tasks that have subtasks) are left out.
    /// </summary>
    public static HashSet<int> CriticalPath(IReadOnlyList<TaskItem> tasks)
    {
        var parents = tasks.Where(t => t.ParentId.HasValue).Select(t => t.ParentId!.Value).ToHashSet();
        var dated = tasks.Where(t => t.StartDate.HasValue && t.DueDate.HasValue && !parents.Contains(t.Id)).ToList();
        var result = new HashSet<int>();
        if (dated.Count == 0) return result;

        var ids = dated.Select(t => t.Id).ToHashSet();
        var byId = dated.ToDictionary(t => t.Id);
        var succ = dated.SelectMany(t => t.DependsOn.Where(ids.Contains).Select(d => (Pred: d, Succ: t.Id)))
                        .GroupBy(x => x.Pred).ToDictionary(g => g.Key, g => g.Select(x => x.Succ).ToList());
        var projectEnd = dated.Max(t => t.DueDate!.Value.Date);

        // Topological order (Kahn). Cycles are prevented elsewhere, but stay safe.
        var indeg = dated.ToDictionary(t => t.Id, t => t.DependsOn.Count(ids.Contains));
        var queue = new Queue<int>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<int>();
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            order.Add(n);
            if (succ.TryGetValue(n, out var ss))
                foreach (var s in ss)
                    if (--indeg[s] == 0) queue.Enqueue(s);
        }
        foreach (var id in ids.Where(i => !order.Contains(i))) order.Add(id);

        // Backward pass: latest finish allowed without moving the project end.
        var latestFinish = new Dictionary<int, DateTime>();
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var id = order[i];
            var lf = projectEnd;
            if (succ.TryGetValue(id, out var ss))
                foreach (var s in ss)
                {
                    if (!latestFinish.TryGetValue(s, out var sLf)) continue;
                    var st = byId[s];
                    var sLatestStart = sLf.AddDays(-(Duration(st.StartDate, st.DueDate) - 1));
                    var candidate = sLatestStart.AddDays(-1);
                    if (candidate < lf) lf = candidate;
                }
            latestFinish[id] = lf;
        }

        foreach (var t in dated)
            if (!t.IsCompleted && (latestFinish[t.Id] - t.DueDate!.Value.Date).Days <= 0)
                result.Add(t.Id);
        return result;
    }
}

public sealed record HealthInfo(ProjectHealth Level, string Reason, DateTime? ForecastEnd, int VarianceDays, int? ExpectedProgress, int Progress)
{
    public string LevelText => Level switch
    {
        ProjectHealth.OnTrack => "On Track",
        ProjectHealth.AtRisk => "At Risk",
        _ => Level.ToString()
    };
}

/// <summary>Project health rules: On Track, At Risk, Delayed, Completed.</summary>
public static class Tracking
{
    public static HealthInfo Assess(Project p, IReadOnlyCollection<TaskItem> tasks, IReadOnlyCollection<Milestone> milestones)
    {
        var today = DateTime.Today;
        var progress = Metrics.ProjectProgress(p, tasks);
        if (p.Status == ProjectStatus.Completed)
            return new HealthInfo(ProjectHealth.Completed, "Project completed.", null, 0, null, 100);
        if (p.Status == ProjectStatus.Cancelled)
            return new HealthInfo(ProjectHealth.Cancelled, "Project cancelled.", null, 0, null, progress);

        var open = tasks.Where(t => !t.IsCompleted).ToList();
        DateTime? forecast = open.Where(t => t.DueDate.HasValue).Select(t => t.DueDate!.Value.Date).DefaultIfEmpty().Max();
        if (forecast == default(DateTime)) forecast = null;
        if (open.Any(t => t.IsOverdue) && (forecast == null || forecast < today)) forecast = today;

        int variance = p.TargetDate.HasValue && forecast.HasValue ? (forecast.Value - p.TargetDate.Value.Date).Days : 0;

        int? expected = null;
        if (p.StartDate.HasValue && p.TargetDate.HasValue && p.TargetDate.Value.Date >= p.StartDate.Value.Date && today >= p.StartDate.Value.Date)
        {
            double total = (p.TargetDate.Value.Date - p.StartDate.Value.Date).Days + 1;
            double elapsed = (today - p.StartDate.Value.Date).Days + 1;
            expected = (int)Math.Round(Math.Clamp(elapsed / total, 0, 1) * 100);
        }

        var delayed = new List<string>();
        var risk = new List<string>();
        if (p.TargetDate.HasValue && p.TargetDate.Value.Date < today && open.Count > 0)
            delayed.Add($"target date {p.TargetDate:dd MMM} has passed");
        else if (variance > 0)
            delayed.Add($"tasks now finish {variance} day{(variance == 1 ? "" : "s")} after the target date");

        int overdue = open.Count(t => t.IsOverdue);
        if (overdue > 0) risk.Add($"{overdue} overdue task{(overdue == 1 ? "" : "s")}");
        int lateMilestones = milestones.Count(m => m.IsOverdue);
        if (lateMilestones > 0) risk.Add($"{lateMilestones} overdue milestone{(lateMilestones == 1 ? "" : "s")}");
        if (expected.HasValue && tasks.Count > 0 && expected.Value - progress >= 20)
            risk.Add($"{progress}% done but {expected}% of the time has passed");
        if (p.TargetDate.HasValue && p.TargetDate.Value.Date >= today && p.TargetDate.Value.Date <= today.AddDays(7) && progress < 80)
            risk.Add($"due within a week at {progress}%");

        if (delayed.Count > 0)
            return new HealthInfo(ProjectHealth.Delayed, Cap(string.Join("; ", delayed.Concat(risk).Take(3))), forecast, variance, expected, progress);
        if (risk.Count > 0)
            return new HealthInfo(ProjectHealth.AtRisk, Cap(string.Join("; ", risk.Take(3))), forecast, variance, expected, progress);
        return new HealthInfo(ProjectHealth.OnTrack,
            tasks.Count == 0 ? "No tasks yet." : "Progress and dates are on schedule.", forecast, variance, expected, progress);
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..] + ".";
}
