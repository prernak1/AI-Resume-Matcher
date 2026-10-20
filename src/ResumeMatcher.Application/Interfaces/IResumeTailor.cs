using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

/// <summary>
/// Rewrites a resume so it targets a specific job description,
/// without inventing skills or experience that the candidate does not have.
/// </summary>
public interface IResumeTailor
{
    Task<TailoredResume> TailorAsync(
        string resumeText,
        string jobTitle,
        string jobDescription,
        CancellationToken cancellationToken = default);
}
