using ResumeMatcher.Application.Interfaces;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>
/// Extracts plain text from PDF files using the PdfPig library.
/// PdfPig is open-source, requires no license, and works without any external process.
///
/// Text is rebuilt line by line from word positions. PdfPig's page.Text runs lines together
/// ("...Integrations" + "email@x.com" becomes one token), which breaks contact-detail extraction.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    // Words whose baselines differ by less than this (in PDF points) belong to the same line
    private const double LineTolerance = 3.0;

    public string Extract(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            AppendLines(page, sb);
        }

        return sb.ToString().Trim();
    }

    private static void AppendLines(Page page, StringBuilder sb)
    {
        var words = page.GetWords()
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();

        var line = new List<Word>();
        double lineBottom = 0;

        void Flush()
        {
            if (line.Count == 0) return;
            sb.AppendLine(string.Join(' ', line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
            line.Clear();
        }

        foreach (var word in words)
        {
            if (line.Count > 0 && Math.Abs(word.BoundingBox.Bottom - lineBottom) > LineTolerance)
                Flush();

            if (line.Count == 0) lineBottom = word.BoundingBox.Bottom;
            line.Add(word);
        }
        Flush();
    }
}
