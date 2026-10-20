using System.Text.RegularExpressions;
using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>
/// Pulls contact details out of raw resume text with simple patterns.
/// Anything it can't find is left blank — the UI lets the user correct or complete the profile.
/// </summary>
public sealed class RegexProfileExtractor : IProfileExtractor
{
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"(?<![\d])(\+?\d[\d\s().\-]{8,16}\d)(?![\d])", RegexOptions.Compiled);
    private static readonly Regex LinkedIn = new(@"(?:https?://)?(?:[a-z]{2,3}\.)?linkedin\.com/in/[A-Za-z0-9\-_%]+/?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GitHub = new(@"(?:https?://)?(?:www\.)?github\.com/[A-Za-z0-9\-_]+/?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AnyUrl = new(@"(?:https?://)[^\s,;|)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Bare "portfolio.example.io/path" links (no scheme); the path requirement avoids matching email domains
    private static readonly Regex BareUrl = new(@"(?<![@\w./-])(?:[a-z0-9\-]+\.)+[a-z]{2,}/[^\s,;|)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NotAWebsite = new(@"linkedin\.com|github\.com|leetcode|hackerrank|codechef|codeforces|geeksforgeeks", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CityLine = new(@"^[A-Z][A-Za-z.\- ]+,\s*[A-Z][A-Za-z.\- ]+$", RegexOptions.Compiled);
    private static readonly Regex NameLine = new(@"^[A-Za-z][A-Za-z.'\-]*(?:\s+[A-Za-z][A-Za-z.'\-]*){1,3}$", RegexOptions.Compiled);

    public CandidateProfile Extract(string resumeText)
    {
        var text = resumeText ?? string.Empty;

        var email = Email.Match(text).Value;
        var phone = FindPhone(text);
        var linkedIn = Normalize(LinkedIn.Match(text).Value);
        var gitHub = Normalize(GitHub.Match(text).Value);
        var website = AnyUrl.Matches(text).Concat(BareUrl.Matches(text))
            .Select(m => m.Value.TrimEnd('.', '/'))
            .FirstOrDefault(u => !NotAWebsite.IsMatch(u)) ?? string.Empty;
        if (website.Length > 0 && !website.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            website = "https://" + website;

        var fullName = FindName(text);
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : string.Empty;
        var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

        return new CandidateProfile(fullName, first, last, email, phone, FindLocation(text), linkedIn, gitHub, website);
    }

    // Looks for a "City, Country" segment in the header block (e.g. "Bengaluru, India | Open to relocate")
    private static string FindLocation(string text)
    {
        var header = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8);
        foreach (var line in header)
            foreach (var segment in line.Split('|', StringSplitOptions.TrimEntries))
                if (segment.Length <= 40 && CityLine.IsMatch(segment))
                    return segment;
        return string.Empty;
    }

    private static string FindName(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8);
        foreach (var line in lines)
        {
            if (line.Length > 40 || !NameLine.IsMatch(line)) continue;
            return line == line.ToUpperInvariant()
                ? System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(line.ToLowerInvariant())
                : line;
        }
        return string.Empty;
    }

    private static string FindPhone(string text)
    {
        foreach (Match m in Phone.Matches(text))
        {
            var digits = m.Value.Count(char.IsDigit);
            if (digits is >= 10 and <= 15) return m.Value.Trim();
        }
        return string.Empty;
    }

    private static string Normalize(string url)
        => string.IsNullOrEmpty(url) ? string.Empty
         : url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url.TrimEnd('/') : "https://" + url.TrimEnd('/');
}
