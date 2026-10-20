using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

public sealed record ApplyPrepareResult(
    string SessionId,
    List<string> FilledFields,
    List<string> MissingRequiredFields,
    bool ResumeUploaded);

public sealed record ApplySubmitResult(string Status, bool Confirmed, string Message);

/// <summary>
/// Opens a job's application form in a real browser window, fills it from the candidate profile,
/// uploads the resume, and then waits. Nothing is submitted until <see cref="SubmitAsync"/> is called.
/// </summary>
public interface IApplyAutomation
{
    Task<ApplyPrepareResult> PrepareAsync(
        string applyUrl,
        CandidateProfile profile,
        byte[] resumePdf,
        string resumeFileName,
        CancellationToken cancellationToken = default);

    Task<ApplySubmitResult> SubmitAsync(string sessionId, CancellationToken cancellationToken = default);

    Task CancelAsync(string sessionId);
}
