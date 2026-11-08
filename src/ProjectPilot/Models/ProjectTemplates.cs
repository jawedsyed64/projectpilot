namespace ProjectPilot.Models;

/// <summary>A task inside a template. StartDay is relative to the project start; DependsOn are indexes of earlier tasks.</summary>
public sealed class TemplateTask
{
    public string Title { get; init; } = "";
    public int StartDay { get; init; }
    public int Days { get; init; } = 1;
    public Priority Priority { get; init; } = Priority.Medium;
    public int[] DependsOn { get; init; } = Array.Empty<int>();
}

public sealed class ProjectTemplate
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<TemplateTask> Tasks { get; init; } = new();
    public override string ToString() => Name;
}

public static class ProjectTemplates
{
    private static TemplateTask T(string title, int start, int days, Priority p, params int[] deps) =>
        new() { Title = title, StartDay = start, Days = days, Priority = p, DependsOn = deps };

    public static IReadOnlyList<ProjectTemplate> All { get; } = new List<ProjectTemplate>
    {
        new()
        {
            Name = "Blank project",
            Description = "Start from scratch with no tasks.",
        },
        new()
        {
            Name = "Software development",
            Description = "Requirements → design → build → test → release (about 6 weeks).",
            Tasks =
            {
                T("Gather and document requirements", 0, 5, Priority.High),
                T("Design architecture and user interface", 5, 7, Priority.High, 0),
                T("Set up development environment", 5, 2, Priority.Medium, 0),
                T("Implement core features", 12, 15, Priority.High, 1, 2),
                T("Write and run automated tests", 27, 5, Priority.Medium, 3),
                T("User acceptance testing", 32, 5, Priority.High, 4),
                T("Deployment and release", 37, 2, Priority.Critical, 5),
                T("Post-release review", 40, 2, Priority.Low, 6),
            }
        },
        new()
        {
            Name = "Marketing campaign",
            Description = "Goals, budget, content, launch and reporting (about 5 weeks).",
            Tasks =
            {
                T("Define goals and target audience", 0, 3, Priority.High),
                T("Plan budget and channels", 3, 3, Priority.High, 0),
                T("Create content and creatives", 6, 10, Priority.High, 1),
                T("Set up ads, email and social channels", 12, 4, Priority.Medium, 1),
                T("Launch campaign", 16, 1, Priority.Critical, 2, 3),
                T("Monitor results and optimise", 17, 14, Priority.Medium, 4),
                T("Final campaign report", 31, 3, Priority.Medium, 5),
            }
        },
        new()
        {
            Name = "Event planning",
            Description = "Scope, venue, vendors, invitations and follow-up (about 8 weeks).",
            Tasks =
            {
                T("Define event scope and budget", 0, 5, Priority.High),
                T("Book venue", 5, 7, Priority.Critical, 0),
                T("Arrange vendors (catering, sound, decoration)", 12, 10, Priority.High, 1),
                T("Send invitations", 12, 3, Priority.High, 1),
                T("Confirm attendees and logistics", 40, 10, Priority.Medium, 2, 3),
                T("Event day", 52, 1, Priority.Critical, 4),
                T("Post-event follow-up and thank-yous", 53, 4, Priority.Low, 5),
            }
        },
        new()
        {
            Name = "Home renovation / construction",
            Description = "Scope, quotes, permits, materials, work and inspection (about 10 weeks).",
            Tasks =
            {
                T("Define scope and budget", 0, 5, Priority.High),
                T("Collect contractor quotes", 5, 10, Priority.High, 0),
                T("Obtain permits and approvals", 10, 14, Priority.Critical, 0),
                T("Purchase materials", 15, 7, Priority.Medium, 1),
                T("Demolition and site preparation", 24, 5, Priority.Medium, 2, 3),
                T("Construction work", 29, 30, Priority.High, 4),
                T("Inspection and finishing", 59, 7, Priority.High, 5),
                T("Final clean-up and handover", 66, 3, Priority.Medium, 6),
            }
        },
    };
}
