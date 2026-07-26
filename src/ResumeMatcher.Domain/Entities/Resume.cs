namespace ResumeMatcher.Domain.Entities;

/// <summary>
/// Represents an uploaded resume and its extracted AI analysis.
///
/// WHY HERE (Domain, not Application):
/// Resume is a core business concept — it IS the thing our application
/// reasons about. It belongs in Domain regardless of where it is stored.
///
/// DESIGN — Private setters + factory method:
/// The only way to create a Resume is through Resume.Create().
/// This guarantees a Resume is always in a valid state.
/// You cannot create an empty Resume with no file name.
/// </summary>
public sealed class Resume
{
    public Guid Id { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string RawText { get; private set; } = string.Empty;
    public DateTime UploadedAt { get; private set; }

    // Populated after AI analysis
    public ResumeAnalysis? Analysis { get; private set; }

    private Resume() { } // Required by EF Core

    public static Resume Create(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty.", nameof(fileName));

        return new Resume
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            UploadedAt = DateTime.UtcNow
        };
    }

    public void SetExtractedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Extracted text cannot be empty.", nameof(text));

        RawText = text;
    }

    public void SetAnalysis(ResumeAnalysis analysis)
    {
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
    }
}
