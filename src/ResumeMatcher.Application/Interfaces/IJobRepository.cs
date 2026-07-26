using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

/// <summary>
/// Defines the contract for accessing job data.
///
/// In Step 1, this returns hardcoded sample jobs.
/// In Step 3, this will query PostgreSQL via Entity Framework Core.
/// The Application layer never changes — only the Infrastructure implementation does.
/// </summary>
public interface IJobRepository
{
    Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken cancellationToken = default);
}
