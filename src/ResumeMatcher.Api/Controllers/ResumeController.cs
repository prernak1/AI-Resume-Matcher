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
    private readonly IJobRepository _jobRepository;

    public ResumeController(
        IPdfTextExtractor pdfExtractor,
        IResumeAnalyzer analyzer,
        IJobMatcher matcher,
        IJobRepository jobRepository)
    {
        _pdfExtractor = pdfExtractor;
        _analyzer = analyzer;
        _matcher = matcher;
        _jobRepository = jobRepository;
    }

    /// <summary>
    /// Upload a resume PDF and receive ranked job matches.
    /// </summary>
    /// <param name="file">PDF file (max 5 MB).</param>
    /// <param name="topN">Number of top matches to return (default 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("match")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ResumeMatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Match(
        IFormFile file,
        [FromQuery] int topN = 5,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ProblemDetails { Title = "No file uploaded.", Status = 400 });

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Only PDF files are accepted.", Status = 400 });

        const long maxBytes = 5 * 1024 * 1024; // 5 MB
        if (file.Length > maxBytes)
            return BadRequest(new ProblemDetails { Title = "File exceeds the 5 MB limit.", Status = 400 });

        // 1. Extract text from PDF
        string resumeText;
        using (var stream = file.OpenReadStream())
        {
            resumeText = _pdfExtractor.Extract(stream);
        }

        if (string.IsNullOrWhiteSpace(resumeText))
            return BadRequest(new ProblemDetails { Title = "Could not extract any text from the PDF.", Status = 400 });

        // 2. Analyze resume with AI / keyword extraction
        var analysis = await _analyzer.AnalyzeAsync(resumeText, cancellationToken);

        // 3. Fetch all jobs and match
        var jobs = await _jobRepository.GetAllAsync(cancellationToken);
        var matches = _matcher.Match(analysis, jobs, topN);

        // 4. Build response
        var response = new ResumeMatchResponse(
            FileName: file.FileName,
            ExtractedTextLength: resumeText.Length,
            Analysis: new ResumeAnalysisDto(
                Skills: analysis.Skills,
                Technologies: analysis.Technologies,
                Education: analysis.Education,
                YearsOfExperience: analysis.YearsOfExperience,
                SuggestedRoles: analysis.SuggestedRoles,
                Summary: analysis.Summary),
            TopMatches: matches.Select(m => new JobMatchDto(
                JobTitle: m.Job.Title,
                JobDescription: m.Job.Description,
                MatchPercentage: m.MatchPercentage,
                MatchingSkills: m.MatchingSkills,
                MissingSkills: m.MissingSkills,
                Reason: m.Reason,
                ApplyUrl: m.Job.ApplyUrl)).ToList());

        return Ok(response);
    }
}

// ---------------------------------------------------------------
// Response DTOs (defined here to keep the surface area small)
// ---------------------------------------------------------------

public sealed record ResumeMatchResponse(
    string FileName,
    int ExtractedTextLength,
    ResumeAnalysisDto Analysis,
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
    string? ApplyUrl);
