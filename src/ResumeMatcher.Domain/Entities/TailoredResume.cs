namespace ResumeMatcher.Domain.Entities;

/// <summary>
/// A resume rewritten to target one specific job description.
/// Contains only facts present in the original resume, re-worded and re-ordered.
/// </summary>
public sealed record TailoredResume(
    string FullName,
    string Contact,
    string Summary,
    List<string> Skills,
    List<TailoredExperience> Experience,
    List<string> Projects,
    List<string> Education,
    List<string> Certifications);

public sealed record TailoredExperience(
    string Title,
    string Company,
    string Period,
    List<string> Bullets);
