namespace ResumeMatcher.Domain.Entities;

/// <summary>Contact details used to fill application forms. Extracted from the resume, editable by the user.</summary>
public sealed record CandidateProfile(
    string FullName,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string Location,
    string LinkedIn,
    string GitHub,
    string Website);
