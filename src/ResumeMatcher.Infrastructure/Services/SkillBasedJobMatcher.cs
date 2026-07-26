using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>
/// Matches a resume's extracted skills against job required/preferred skill lists.
///
/// Scoring algorithm:
///   Required skills matched  = 70% of score
///   Preferred skills matched = 30% of score
///
/// Returns up to topN jobs sorted by match percentage descending.
/// </summary>
public sealed class SkillBasedJobMatcher : IJobMatcher
{
    public IReadOnlyList<JobMatch> Match(ResumeAnalysis analysis, IReadOnlyList<Job> jobs, int topN = 5)
    {
        var resumeSkills = new HashSet<string>(
            analysis.Skills.Concat(analysis.Technologies),
            StringComparer.OrdinalIgnoreCase);

        return jobs
            .Select(job => ScoreJob(job, resumeSkills))
            .Where(m => m.MatchPercentage > 0 && m.MatchingSkills.Count >= 2)
            .OrderByDescending(m => m.MatchPercentage)
            .Take(topN)
            .ToList()
            .AsReadOnly();
    }

    private static JobMatch ScoreJob(Job job, HashSet<string> resumeSkills)
    {
        var matchingRequired = job.RequiredSkills
            .Where(s => resumeSkills.Contains(s))
            .ToList();

        var matchingPreferred = job.PreferredSkills
            .Where(s => resumeSkills.Contains(s))
            .ToList();

        var missingRequired = job.RequiredSkills
            .Where(s => !resumeSkills.Contains(s))
            .ToList();

        // Weighted score: required 70%, preferred 30%
        // No required skills means no skill data to compare → 0, not a free 70 points.
        double requiredScore = job.RequiredSkills.Count > 0
            ? (double)matchingRequired.Count / job.RequiredSkills.Count * 70
            : 0;

        double preferredScore = job.PreferredSkills.Count > 0
            ? (double)matchingPreferred.Count / job.PreferredSkills.Count * 30
            : 0;

        // Confidence weight: jobs where fewer than 4 skills were extractable
        // from the description get a proportionally lower score ceiling.
        // This prevents single-keyword matches inflating non-technical roles to 100%.
        var totalExtracted = job.RequiredSkills.Count + job.PreferredSkills.Count;
        var confidence = Math.Min(1.0, totalExtracted / 4.0);

        var matchPercentage = (int)Math.Round((requiredScore + preferredScore) * confidence);

        var allMatching = matchingRequired.Concat(matchingPreferred).Distinct().ToList();

        var reason = BuildReason(matchingRequired, missingRequired, matchingPreferred, job.Title);

        return new JobMatch
        {
            Job = job,
            MatchPercentage = matchPercentage,
            MatchingSkills = allMatching,
            MissingSkills = missingRequired,
            Reason = reason
        };
    }

    private static string BuildReason(
        List<string> matchingRequired,
        List<string> missingRequired,
        List<string> matchingPreferred,
        string jobTitle)
    {
        var parts = new List<string>();

        if (matchingRequired.Any())
            parts.Add($"Has {matchingRequired.Count} of {matchingRequired.Count + missingRequired.Count} required skills ({string.Join(", ", matchingRequired.Take(3))}{(matchingRequired.Count > 3 ? "..." : "")}).");

        if (missingRequired.Any())
            parts.Add($"Missing required: {string.Join(", ", missingRequired.Take(3))}{(missingRequired.Count > 3 ? "..." : "")}.");

        if (matchingPreferred.Any())
            parts.Add($"Bonus: {matchingPreferred.Count} preferred skill(s) matched.");

        return parts.Any() ? string.Join(" ", parts) : $"Low overlap with {jobTitle} requirements.";
    }
}
