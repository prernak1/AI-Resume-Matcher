using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

/// <summary>
/// Defines the contract for accessing job data.
/// Pass an optional <paramref name="boardToken"/> to override the configured source at request time.
/// </summary>
public interface IJobRepository
{
    Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Job>> GetAllAsync(string boardToken, CancellationToken cancellationToken = default);
}
