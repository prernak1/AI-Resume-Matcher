using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;
using System.Text.RegularExpressions;

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
    // The skill bank is tech-only, so non-technical roles that merely mention "SQL" or "Agile"
    // must not be ranked as matches. Title decides first; skills only break ties for unusual titles.
    private static readonly Regex NonTechnicalTitle = new(
        @"\b(sales|account (executive|manager)|marketing|recruit\w*|talent|legal|counsel|attorney|paralegal|finance|financial|accountant|accounting|audit|tax|payroll|people|hr|human resources|customer success|support specialist|operations manager|business development|partnerships?|communications|designer|writer|compliance|risk analyst|strategy)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TechnicalTitle = new(
        @"(engineer|developer|software|backend|back-end|full[- ]?stack|devops|\bsre\b|reliability|platform|infrastructure|architect|programmer|machine learning|\bml\b|\bai\b|\bllm\b|cloud|security|systems|\bswe\b|sdet|data (scientist|analyst|engineer))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StrongTechnicalTitle = new(
        @"(software|developer|backend|back-end|full[- ]?stack|devops|\bsre\b|machine learning)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // People-management, executive and pre-sales roles are never a fit for an individual-contributor resume
    private static readonly Regex ManagementTitle = new(
        @"\b(director|vp|vice president|head of|chief|manager|managers|fp&a|solutions? (engineer|architect)|sales engineer|business (systems? )?analyst|process automation)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeniorIcTitle = new(
        @"\b(staff|principal|distinguished|fellow)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Staff/Principal roles need roughly this many years; below it they are stretch roles, not matches
    private const int SeniorIcMinYears = 7;

    private static readonly Regex EntryLevelTitle = new(
        @"\b(intern|internship|new grad|graduate program|apprentice|co-?op)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<JobMatch> Match(ResumeAnalysis analysis, IReadOnlyList<Job> jobs, int topN = 5)
    {
        var resumeSkills = new HashSet<string>(
            analysis.Skills.Concat(analysis.Technologies),
            StringComparer.OrdinalIgnoreCase);

        return jobs
            .Where(j => IsRelevantTitle(j, analysis.YearsOfExperience))
            .Select(job => ScoreJob(job, resumeSkills))
            .Where(m => m.MatchPercentage > 0 && m.MatchingSkills.Count >= 2)
            // The same role is often posted once per location — keep only the best-scoring copy
            .GroupBy(m => (m.Job.Source.ToLowerInvariant(), m.Job.Title.ToLowerInvariant()))
            .Select(g => g.OrderByDescending(m => m.MatchPercentage).First())
            .OrderByDescending(m => m.MatchPercentage)
            .Take(topN)
            .ToList()
            .AsReadOnly();
    }

    private static bool IsRelevantTitle(Job job, int yearsOfExperience)
    {
        if (ManagementTitle.IsMatch(job.Title)) return false;

        // yearsOfExperience == 0 means "not detected" — don't filter on seniority then
        if (yearsOfExperience is > 0 and < SeniorIcMinYears && SeniorIcTitle.IsMatch(job.Title)) return false;
        if (yearsOfExperience >= 2 && EntryLevelTitle.IsMatch(job.Title)) return false;

        // "Software Engineer, People Platform" stays; "Strategic Sales Representative" goes
        if (NonTechnicalTitle.IsMatch(job.Title) && !StrongTechnicalTitle.IsMatch(job.Title)) return false;
        return TechnicalTitle.IsMatch(job.Title) || job.RequiredSkills.Count >= 3;
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
