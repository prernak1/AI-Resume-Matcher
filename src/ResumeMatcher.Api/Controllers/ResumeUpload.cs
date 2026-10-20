using Microsoft.AspNetCore.Mvc;
using ResumeMatcher.Application.Interfaces;

namespace ResumeMatcher.Api.Controllers;

/// <summary>Shared validation + text extraction for uploaded resume PDFs.</summary>
internal static class ResumeUpload
{
    public const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    public static (string Text, ProblemDetails? Error) Read(IFormFile? file, IPdfTextExtractor extractor)
    {
        if (file is null || file.Length == 0)
            return ("", Problem("No file uploaded."));

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return ("", Problem("Only PDF files are accepted."));

        if (file.Length > MaxBytes)
            return ("", Problem("File exceeds the 5 MB limit."));

        string text;
        using (var stream = file.OpenReadStream())
        {
            text = extractor.Extract(stream);
        }

        return string.IsNullOrWhiteSpace(text)
            ? ("", Problem("Could not extract any text from the PDF."))
            : (text, null);
    }

    private static ProblemDetails Problem(string title) => new() { Title = title, Status = 400 };
}
