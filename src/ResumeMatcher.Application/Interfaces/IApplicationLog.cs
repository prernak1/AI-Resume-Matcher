namespace ResumeMatcher.Application.Interfaces;

public sealed record ApplicationRecord(
    string ApplyUrl,
    string JobTitle,
    string Company,
    string Status,
    DateTimeOffset At);

/// <summary>Remembers which jobs were already applied to, so the same job is never applied to twice.</summary>
public interface IApplicationLog
{
    bool HasApplied(string applyUrl);
    void Record(ApplicationRecord record);
    IReadOnlyList<ApplicationRecord> GetAll();
}
