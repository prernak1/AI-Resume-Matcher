using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

/// <summary>
/// Defines the contract for the job matching algorithm.
///
/// WHY NOT HARDCODE THIS LOGIC IN A CONTROLLER:
/// Business logic (matching resumes to jobs) is the core value of this application.
/// It belongs in Application (or Domain), never in a controller.
/// Controllers are for HTTP concerns only — parsing requests, returning responses.
///
/// Keeping this as an interface means:
/// 1. The matching algorithm can be improved without touching API code
/// 2. It is easily unit-testable (mock the interface, test the controller)
/// 3. You could swap to an AI-powered matcher later
/// </summary>
public interface IJobMatcher
{
    /// <summary>
    /// Compares a resume analysis against all available jobs and returns
    /// the top matches sorted by match percentage descending.
    /// </summary>
    IReadOnlyList<JobMatch> Match(ResumeAnalysis analysis, IReadOnlyList<Job> jobs, int topN = 5);
}
