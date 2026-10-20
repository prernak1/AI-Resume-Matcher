using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>Renders a <see cref="TailoredResume"/> as a clean single-column ATS-friendly PDF.</summary>
public sealed class QuestPdfResumeRenderer : IResumePdfRenderer
{
    static QuestPdfResumeRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(TailoredResume r)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial"));

                page.Content().Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Text(r.FullName).FontSize(20).Bold();
                    if (!string.IsNullOrWhiteSpace(r.Contact))
                        col.Item().Text(r.Contact).FontColor(Colors.Grey.Darken2);

                    Section(col, "Summary", () => col.Item().Text(r.Summary));

                    if (r.Skills.Count > 0)
                        Section(col, "Skills", () => col.Item().Text(string.Join(" • ", r.Skills)));

                    if (r.Experience.Count > 0)
                    {
                        Heading(col, "Experience");
                        foreach (var e in r.Experience)
                        {
                            col.Item().ShowEntire().Column(c =>
                            {
                                c.Item().Row(row =>
                                {
                                    row.RelativeItem().Text($"{e.Title}, {e.Company}".Trim(' ', ',')).Bold();
                                    row.AutoItem().Text(e.Period).FontColor(Colors.Grey.Darken2);
                                });
                                foreach (var b in e.Bullets)
                                    c.Item().PaddingLeft(10).Text($"• {b}");
                            });
                        }
                    }

                    if (r.Projects.Count > 0)
                    {
                        Heading(col, "Projects");
                        foreach (var p in r.Projects) col.Item().Text($"• {p}");
                    }

                    if (r.Education.Count > 0)
                    {
                        Heading(col, "Education");
                        foreach (var e in r.Education) col.Item().Text(e);
                    }

                    if (r.Certifications.Count > 0)
                    {
                        Heading(col, "Certifications");
                        foreach (var c in r.Certifications) col.Item().Text($"• {c}");
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void Section(ColumnDescriptor col, string title, Action body)
    {
        Heading(col, title);
        body();
    }

    private static void Heading(ColumnDescriptor col, string title)
    {
        col.Item().PaddingTop(6).Text(title.ToUpperInvariant()).FontSize(11).Bold().FontColor(Colors.Indigo.Darken3);
        col.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
    }
}
