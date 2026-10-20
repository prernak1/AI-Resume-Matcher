using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

/// <param name="Companies">Explicit company tokens. Empty = search every configured company.</param>
/// <param name="PostedWithinDays">0 = no date filter.</param>
/// <param name="Roles">Role keywords (from the resume) used by keyword-based sources.</param>
/// <param name="Location">"india" keeps only India-based jobs. Empty = any location.</param>
public sealed record JobSearchRequest(
    IReadOnlyList<string> Companies,
    int PostedWithinDays,
    IReadOnlyList<string> Roles,
    string? Location = null);

/// <summary>Finds jobs across every configured job source.</summary>
public interface IJobSearchService
{
    Task<IReadOnlyList<Job>> SearchAsync(JobSearchRequest request, CancellationToken cancellationToken = default);
}
