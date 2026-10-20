using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Api.Controllers;

/// <summary>
/// Approval-based auto-apply:
///   1. POST /api/apply/prepare   — tailors the resume, opens the application form in a browser window and fills it.
///   2. POST /api/apply/{id}/submit — the user's approval: clicks Submit in that window.
/// Nothing is ever submitted without step 2.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class ApplyController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IPdfTextExtractor _pdfExtractor;
    private readonly IResumeTailor _tailor;
    private readonly IResumePdfRenderer _pdfRenderer;
    private readonly IApplyAutomation _apply;
    private readonly IApplicationLog _log;
    private readonly string _dataDirectory;

    public ApplyController(
        IPdfTextExtractor pdfExtractor,
        IResumeTailor tailor,
        IResumePdfRenderer pdfRenderer,
        IApplyAutomation apply,
        IApplicationLog log,
        IConfiguration configuration)
    {
        _pdfExtractor = pdfExtractor;
        _tailor = tailor;
        _pdfRenderer = pdfRenderer;
        _apply = apply;
        _log = log;
        _dataDirectory = configuration["Apply:DataDirectory"] is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumeMatcher");
    }

    /// <summary>Tailor the resume for the job, then open and pre-fill the application form (does NOT submit).</summary>
    /// <param name="profile">JSON of the candidate profile (name, email, phone, links).</param>
    /// <param name="tailor">When true (default) the resume is rewritten for the job; otherwise the uploaded PDF is attached as-is.</param>
    [HttpPost("prepare")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PrepareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Prepare(
        IFormFile file,
        [FromForm] string applyUrl,
        [FromForm] string jobTitle,
        [FromForm] string company,
        [FromForm] string jobDescription,
        [FromForm] string profile,
        [FromForm] bool tailor = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applyUrl))
            return BadRequest(new ProblemDetails { Title = "This job has no apply link.", Status = 400 });

        if (_log.HasApplied(applyUrl))
            return Conflict(new ProblemDetails { Title = "You've already applied to this job.", Status = 409 });

        CandidateProfile? candidate;
        try
        {
            candidate = JsonSerializer.Deserialize<CandidateProfile>(profile ?? "", JsonOptions);
        }
        catch (JsonException)
        {
            candidate = null;
        }
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.Email))
            return BadRequest(new ProblemDetails { Title = "Your profile needs at least an email address.", Status = 400 });

        var (resumeText, problem) = ResumeUpload.Read(file, _pdfExtractor);
        if (problem is not null) return BadRequest(problem);

        var fileName = $"Resume_{Safe(company)}_{Safe(jobTitle)}.pdf";
        byte[] pdf;
        if (tailor)
        {
            if (string.IsNullOrWhiteSpace(jobDescription))
                return BadRequest(new ProblemDetails { Title = "A job description is required to tailor the resume.", Status = 400 });

            try
            {
                var tailored = await _tailor.TailorAsync(resumeText, jobTitle ?? string.Empty, jobDescription, cancellationToken);
                pdf = _pdfRenderer.Render(tailored);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails { Title = ex.Message, Status = 502 });
            }
        }
        else
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            pdf = ms.ToArray();
        }

        var saved = SaveCopy(fileName, pdf);

        try
        {
            var result = await _apply.PrepareAsync(applyUrl, candidate, pdf, fileName, cancellationToken);
            return Ok(new PrepareResponse(result.SessionId, result.FilledFields, result.MissingRequiredFields,
                result.ResumeUploaded, tailor, saved));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ProblemDetails { Title = $"Couldn't open the application form: {ex.Message.Split('\n')[0]}", Status = 502 });
        }
    }

    /// <summary>User approval: click Submit in the prepared browser window.</summary>
    [HttpPost("{sessionId}/submit")]
    public async Task<IActionResult> Submit(string sessionId, [FromBody] ApplyJobInfo job, CancellationToken cancellationToken)
    {
        var result = await _apply.SubmitAsync(sessionId, cancellationToken);
        _log.Record(new ApplicationRecord(job.ApplyUrl, job.JobTitle, job.Company,
            result.Confirmed ? "Submitted" : $"Not confirmed ({result.Status})", DateTimeOffset.UtcNow));
        return Ok(result);
    }

    /// <summary>The user finished the application by hand in the open window.</summary>
    [HttpPost("{sessionId}/mark-applied")]
    public async Task<IActionResult> MarkApplied(string sessionId, [FromBody] ApplyJobInfo job)
    {
        _log.Record(new ApplicationRecord(job.ApplyUrl, job.JobTitle, job.Company, "Submitted", DateTimeOffset.UtcNow));
        await _apply.CancelAsync(sessionId);
        return NoContent();
    }

    [HttpPost("{sessionId}/cancel")]
    public async Task<IActionResult> Cancel(string sessionId)
    {
        await _apply.CancelAsync(sessionId);
        return NoContent();
    }

    /// <summary>Every application attempt so far.</summary>
    [HttpGet("log")]
    public IActionResult Log() => Ok(_log.GetAll());

    private string? SaveCopy(string fileName, byte[] pdf)
    {
        try
        {
            var dir = Path.Combine(_dataDirectory, "resumes");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            System.IO.File.WriteAllBytes(path, pdf);
            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Safe(string? s)
    {
        var cleaned = new string((s ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
        return cleaned.Length > 0 ? cleaned : "job";
    }
}

public sealed record PrepareResponse(
    string SessionId,
    List<string> FilledFields,
    List<string> MissingRequiredFields,
    bool ResumeUploaded,
    bool Tailored,
    string? SavedResumePath);

public sealed record ApplyJobInfo(string ApplyUrl, string JobTitle, string Company);
