namespace ResumeMatcher.Domain.Entities;

/// <summary>
/// A job listing that resumes are matched against.
///
/// In Step 1, jobs are seeded directly in code (no external source yet).
/// In a later step, we will seed them into PostgreSQL and query from there.
///
/// REQUIRED SKILLS: The skills a candidate MUST have.
/// PREFERRED SKILLS: Nice to have — used to distinguish candidates.
/// </summary>
public sealed class Job
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? ApplyUrl { get; private set; }
    public string Source { get; private set; } = string.Empty;   // board token or "sample"
    public DateTimeOffset? PostedAt { get; private set; }
    public string Platform { get; private set; } = string.Empty;  // greenhouse | lever | ashby | adzuna
    public string Location { get; private set; } = string.Empty; // all known locations, joined with "; "
    public string FullDescription { get; private set; } = string.Empty;
    public List<string> RequiredSkills { get; private set; } = [];
    public List<string> PreferredSkills { get; private set; } = [];

    private Job() { }

    public static Job Create(
        string title,
        string description,
        List<string> requiredSkills,
        List<string>? preferredSkills = null,
        string? applyUrl = null,
        string source = "",
        DateTimeOffset? postedAt = null,
        string? fullDescription = null,
        string platform = "",
        string location = "")
    {
        return new Job
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            ApplyUrl = applyUrl,
            Source = source,
            PostedAt = postedAt,
            Platform = platform,
            Location = location,
            FullDescription = string.IsNullOrWhiteSpace(fullDescription) ? description : fullDescription,
            RequiredSkills = requiredSkills,
            PreferredSkills = preferredSkills ?? []
        };
    }
}
