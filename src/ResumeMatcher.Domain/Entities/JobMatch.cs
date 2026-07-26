namespace ResumeMatcher.Domain.Entities;

/// <summary>
/// The result of matching one resume against one job.
/// This is a VALUE OBJECT — it has no identity of its own.
/// Two JobMatch objects with the same data are equal.
/// It is produced by the matching algorithm and returned to the caller.
/// It does not need to be stored in a database (for now).
/// </summary>
public sealed class JobMatch
{
    public Job Job { get; init; } = null!;
    public int MatchPercentage { get; init; }
    public List<string> MatchingSkills { get; init; } = [];
    public List<string> MissingSkills { get; init; } = [];
    public string Reason { get; init; } = string.Empty;
}
