namespace ResumeMatcher.Domain.Entities;

/// <summary>
/// The structured result of AI analysis on a resume.
///
/// WHY A SEPARATE CLASS (not properties on Resume):
/// Resume = the uploaded file + its raw text.
/// ResumeAnalysis = what AI concluded about it.
/// Separating them keeps each class focused on one thing.
///
/// In future steps, we will store this as a JSON column in PostgreSQL,
/// mapping the whole object as a single column using EF Core's
/// JSON column support (EF 8+).
/// </summary>
public sealed class ResumeAnalysis
{
    public List<string> Skills { get; init; } = [];
    public List<string> Technologies { get; init; } = [];
    public string Education { get; init; } = string.Empty;
    public int YearsOfExperience { get; init; }
    public List<string> SuggestedRoles { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}
