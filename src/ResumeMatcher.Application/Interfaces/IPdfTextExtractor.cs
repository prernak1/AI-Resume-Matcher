namespace ResumeMatcher.Application.Interfaces;

/// <summary>
/// Defines the contract for extracting text from uploaded PDF files.
///
/// WHY AN INTERFACE:
/// The actual PDF library (PdfPig, iTextSharp, etc.) lives in Infrastructure.
/// Application only says "I need to be able to extract text from a stream."
/// The specific library used can change without touching any Application code.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extracts all text content from a PDF stream.
    /// </summary>
    /// <param name="pdfStream">The PDF file stream. Must be readable.</param>
    /// <returns>Plain text content of the PDF.</returns>
    string Extract(Stream pdfStream);
}
