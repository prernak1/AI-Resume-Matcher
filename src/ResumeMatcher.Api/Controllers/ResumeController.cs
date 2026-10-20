using Microsoft.AspNetCore.Mvc;
using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Api.Controllers;

/// <summary>
/// Handles resume upload and job matching.
/// POST /api/resume/match — upload a PDF, get back ranked job matches.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class ResumeController : ControllerBase
{
    private readonly IPdfTextExtractor _pdfExtractor;
    private readonly IResumeAnalyzer _analyzer;
    private readonly IJobMatcher _matcher;
    private readonly IJobSearchService _jobSearch;
    private readonly IProfileExtractor _profileExtractor;
    private readonly IResumeTailor _tailor;
    private readonly IResumePdfRenderer _pdfRenderer;

    public ResumeController(
        IPdfTextExtractor pdfExtractor,
        IResumeAnalyzer analyzer,
        IJobMatcher matcher,
        IJobSearchService jobSearch,
        IProfileExtractor profileExtractor,
        IResumeTailor tailor,
        IResumePdfRenderer pdfRenderer)
    {
        _pdfExtractor = pdfExtractor;
        _analyzer = analyzer;
        _matcher = matcher;
        _jobSearch = jobSearch;
        _profileExtractor = profileExtractor;
        _tailor = tailor;
        _pdfRenderer = pdfRenderer;
    }

    /// <summary>
    /// Upload a resume PDF and receive ranked job matches across Greenhouse, Lever, Ashby (and Adzuna when configured).
    /// </summary>
    /// <param name="file">PDF file (max 5 MB).</param>
    /// <param name="topN">Number of top matches to return (default 10).</param>
    /// <param name="company">Comma-separated company tokens (e.g. "stripe,openai,palantir"). Leave empty to search every company in companies.json.</param>
    /// <param name="postedWithinDays">When &gt; 0, only jobs first published within this many days are considered (e.g. 3).</param>
    /// <param name="location">"india" keeps only India-based jobs. Empty = any location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("match")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ResumeMatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Match(
        IFormFile file,
        [FromQuery] int topN = 10,
        [FromQuery] string? company = null,
        [FromQuery] int postedWithinDays = 0,
        [FromQuery] string? location = null,
        CancellationToken cancellationToken = default)
    {
        var (resumeText, error) = ReadResume(file);
        if (error is not null) return error;

        var analysis = await _analyzer.AnalyzeAsync(resumeText, cancellationToken);
        var profile = _profileExtractor.Extract(resumeText);

        var tokens = ParseTokens(company);
        var sources = tokens.Count > 0 ? string.Join(", ", tokens) : "all configured companies";

        var jobs = await _jobSearch.SearchAsync(
            new JobSearchRequest(tokens, postedWithinDays, analysis.SuggestedRoles, location),
            cancellationToken);

        var matches = _matcher.Match(analysis, jobs, topN);

        var response = new ResumeMatchResponse(
            FileName: file.FileName,
            Company: sources,
            ExtractedTextLength: resumeText.Length,
            Analysis: new ResumeAnalysisDto(
                Skills: analysis.Skills,
                Technologies: analysis.Technologies,
                Education: analysis.Education,
                YearsOfExperience: analysis.YearsOfExperience,
                SuggestedRoles: analysis.SuggestedRoles,
                Summary: analysis.Summary),
            Profile: profile,
            JobsSearched: jobs.Count,
            TopMatches: matches.Select(m => new JobMatchDto(
                JobTitle: m.Job.Title,
                JobDescription: m.Job.Description,
                MatchPercentage: m.MatchPercentage,
                MatchingSkills: m.MatchingSkills,
                MissingSkills: m.MissingSkills,
                Reason: m.Reason,
                ApplyUrl: m.Job.ApplyUrl,
                Source: m.Job.Source,
                PostedAt: m.Job.PostedAt,
                FullDescription: m.Job.FullDescription,
                Platform: m.Job.Platform)).ToList());

        return Ok(response);
    }

    /// <summary>
    /// Upload a resume PDF plus a job description and receive a PDF resume tailored to that job.
    /// Only facts from the original resume are used.
    /// </summary>
    [HttpPost("tailor")]
    [Consumes("multipart/form-data")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Tailor(
        IFormFile file,
        [FromForm] string jobTitle,
        [FromForm] string jobDescription,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobDescription))
            return BadRequest(new ProblemDetails { Title = "A job description is required.", Status = 400 });

        var (resumeText, error) = ReadResume(file);
        if (error is not null) return error;

        TailoredResume tailored;
        try
        {
            tailored = await _tailor.TailorAsync(resumeText, jobTitle ?? string.Empty, jobDescription, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ProblemDetails { Title = ex.Message, Status = 502 });
        }

        var pdf = _pdfRenderer.Render(tailored);
        var safeTitle = new string((jobTitle ?? "job").Where(char.IsLetterOrDigit).Take(40).ToArray());
        return File(pdf, "application/pdf", $"Resume_{(safeTitle.Length > 0 ? safeTitle : "tailored")}.pdf");
    }

    private (string Text, IActionResult? Error) ReadResume(IFormFile? file)
    {
        var (text, problem) = ResumeUpload.Read(file, _pdfExtractor);
        return (text, problem is null ? null : BadRequest(problem));
    }

    // Parses "stripe, airbnb, shopify" into ["stripe", "airbnb", "shopify"]
    private static List<string> ParseTokens(string? company)
        => (company ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

// ---------------------------------------------------------------
// Response DTOs (defined here to keep the surface area small)
// ---------------------------------------------------------------

public sealed record ResumeMatchResponse(
    string FileName,
    string Company,
    int ExtractedTextLength,
    ResumeAnalysisDto Analysis,
    CandidateProfile Profile,
    int JobsSearched,
    List<JobMatchDto> TopMatches);

public sealed record ResumeAnalysisDto(
    List<string> Skills,
    List<string> Technologies,
    string Education,
    int YearsOfExperience,
    List<string> SuggestedRoles,
    string Summary);

public sealed record JobMatchDto(
    string JobTitle,
    string JobDescription,
    int MatchPercentage,
    List<string> MatchingSkills,
    List<string> MissingSkills,
    string Reason,
    string? ApplyUrl,
    string Source,
    DateTimeOffset? PostedAt,
    string FullDescription,
    string Platform);
