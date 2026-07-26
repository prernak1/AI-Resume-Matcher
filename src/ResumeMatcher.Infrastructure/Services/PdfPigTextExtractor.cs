using ResumeMatcher.Application.Interfaces;
using System.Text;
using UglyToad.PdfPig;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>
/// Extracts plain text from PDF files using the PdfPig library.
/// PdfPig is open-source, requires no license, and works without any external process.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public string Extract(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString().Trim();
    }
}
