using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ResumeMatcher.Infrastructure.Repositories;

/// <summary>
/// Fetches live job listings from the Greenhouse Job Board API.
/// Endpoint: GET https://boards-api.greenhouse.io/v1/boards/{board_token}/jobs?content=true
///
/// Results are cached in-memory for <see cref="CacheMinutes"/> (default 10) to avoid
/// hammering the public API on every resume match request.
///
/// Skill extraction: strips HTML from the job description, then scans for
/// known technology keywords — splitting on "nice to have / preferred" markers
/// to separate required skills from preferred ones.
///
/// Falls back gracefully: if the Greenhouse API is unreachable and a cached
/// result exists (even if stale), that result is returned rather than throwing.
/// </summary>
public sealed class GreenhouseJobRepository : IJobRepository
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _boardToken;
    private readonly int _cacheMinutes;

    // Thread-safe double-checked locking cache
    private IReadOnlyList<Job>? _cache;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // ---------------------------------------------------------------
    // Keyword bank — ordered longest-first to prevent partial matches
    // ---------------------------------------------------------------
    private static readonly string[] AllKeywords =
    [
        "ASP.NET Core", "ASP.NET", ".NET Core", ".NET",
        "Entity Framework Core", "Entity Framework",
        "C#", "F#", "VB.NET",
        "SQL Server", "PostgreSQL", "MySQL", "SQLite", "Oracle",
        "Azure DevOps", "Azure Service Bus", "Azure Functions", "Azure",
        "AWS Lambda", "AWS", "GCP",
        "GitHub Actions", "GitHub", "Git",
        "Docker", "Kubernetes", "Helm",
        "Terraform", "Ansible",
        "React", "Angular", "Vue", "Next.js", "Blazor",
        "Node.js", "TypeScript", "JavaScript",
        "Python", "FastAPI", "Flask", "Django",
        "Java", "Spring Boot", "Spring",
        "Go", "Rust", "Kotlin",
        "REST API", "REST", "GraphQL", "gRPC", "SignalR",
        "RabbitMQ", "Kafka", "Redis",
        "MongoDB", "Elasticsearch",
        "Microservices", "CQRS", "DDD", "Event Sourcing",
        "MediatR", "AutoMapper",
        "xUnit", "NUnit", "MSTest",
        "Selenium", "Playwright", "Cypress",
        "Jenkins", "TeamCity", "CircleCI",
        "CI/CD", "DevOps",
        "Linux", "Bash", "PowerShell",
        "Nginx", "IIS",
        "HTML", "CSS",
        "WPF", "MAUI",
        "Machine Learning", "ML", "OpenAI", "LLM",
        "Agile", "Scrum", "Kanban",
        "TDD", "BDD", "Unit Testing",
        "SOLID", "Design Patterns", "OOP",
        "OAuth", "JWT", "OpenID Connect",
        "SQL", "NoSQL",
    ];

    // Patterns that typically introduce a "nice-to-have" section
    private static readonly Regex PreferredSectionPattern = new(
        @"(nice[\s-]to[\s-]have|preferred qualifications?|bonus|would be a plus|ideally|desirable|advantageous)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Strips all HTML tags and decodes basic entities
    private static readonly Regex HtmlTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);

    public GreenhouseJobRepository(IHttpClientFactory httpFactory, string boardToken, int cacheMinutes = 10)
    {
        _httpFactory = httpFactory;
        _boardToken = boardToken;
        _cacheMinutes = cacheMinutes;
    }

    public async Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Fast path — serve from cache without acquiring lock
        if (_cache is not null && DateTime.UtcNow < _cacheExpiry)
            return _cache;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Re-check after acquiring lock (double-checked locking)
            if (_cache is not null && DateTime.UtcNow < _cacheExpiry)
                return _cache;

            var fetched = await FetchFromGreenhouseAsync(cancellationToken);

            if (fetched.Count > 0)
            {
                _cache = fetched;
                _cacheExpiry = DateTime.UtcNow.AddMinutes(_cacheMinutes);
            }

            // Return stale cache on failure rather than an empty list
            return _cache ?? fetched;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    // Allow per-request board token override (e.g. ?company=airbnb from the UI)
    public async Task<IReadOnlyList<Job>> GetAllAsync(string boardToken, CancellationToken cancellationToken = default)
        => await FetchFromGreenhouseAsync(boardToken, cancellationToken);

    private async Task<IReadOnlyList<Job>> FetchFromGreenhouseAsync(CancellationToken ct)
        => await FetchFromGreenhouseAsync(_boardToken, ct);

    private async Task<IReadOnlyList<Job>> FetchFromGreenhouseAsync(string boardToken, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("Greenhouse");

        GreenhouseResponse? response;
        try
        {
            response = await http.GetFromJsonAsync<GreenhouseResponse>(
                $"v1/boards/{Uri.EscapeDataString(boardToken)}/jobs?content=true",
                ct);
        }
        catch
        {
            // Network failure — caller will get stale cache or empty list
            return _cache ?? [];
        }

        if (response?.Jobs is null or { Count: 0 })
            return [];

        return response.Jobs
            .Where(j => !string.IsNullOrWhiteSpace(j.Title))
            .Select(j => MapToJob(j, boardToken))
            .ToList()
            .AsReadOnly();
    }

    private static Job MapToJob(GreenhouseJob ghJob, string boardToken)
    {
        var plainText = StripHtml(ghJob.Content ?? string.Empty);
        var (required, preferred) = ExtractSkills(plainText);

        // Resolve the best available location:
        // Some boards put "Remote" in location.name and the real city in metadata.
        var location = ResolveLocation(ghJob);
        var isRemote = IsRemoteJob(ghJob);
        var dept = ghJob.Departments?.FirstOrDefault()?.Name;

        var desc = ghJob.Content is not null ? TruncatePlainText(plainText, 300) : ghJob.Title ?? string.Empty;

        // Build prefix: (Dept) [Location · Remote]
        var locParts = new List<string>();
        if (!string.IsNullOrEmpty(location)) locParts.Add(location);
        if (isRemote && !location.Contains("Remote", StringComparison.OrdinalIgnoreCase)) locParts.Add("Remote");
        var locLabel = string.Join(" · ", locParts);

        if (!string.IsNullOrEmpty(locLabel)) desc = $"[{locLabel}] {desc}";
        if (!string.IsNullOrEmpty(dept))     desc = $"({dept}) {desc}";

        return Job.Create(
            title: ghJob.Title!,
            description: desc,
            requiredSkills: required,
            preferredSkills: preferred,
            applyUrl: ghJob.AbsoluteUrl,
            source: boardToken);
    }

    // ---------------------------------------------------------------
    // Location helpers (ported from the Greenhouse integration article)
    // https://medium.com/@sevicdev/integration-with-greenhouse-public-jobs-api-b548b23ad867
    // ---------------------------------------------------------------

    private static readonly Regex WorkModePattern =
        new(@"^(remote|hybrid|on-?site|in-?office)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string ResolveLocation(GreenhouseJob job)
    {
        var primary = job.Location?.Name?.Trim() ?? string.Empty;
        var fromMeta = ExtractMetadataLocation(job.Metadata);

        // Prefer metadata city when the primary field is only a work-mode label
        if (!string.IsNullOrEmpty(fromMeta) &&
            (string.IsNullOrEmpty(primary) || WorkModePattern.IsMatch(primary)))
            return fromMeta;

        return !string.IsNullOrEmpty(primary) ? primary : fromMeta ?? string.Empty;
    }

    private static string? ExtractMetadataLocation(List<GreenhouseMetadata>? metadata)
    {
        const string key = "Job Posting Location";
        var item = metadata?.FirstOrDefault(m =>
            string.Equals(m.Name?.Trim(), key, StringComparison.OrdinalIgnoreCase));

        return item?.Value?.Trim() is { Length: > 0 } v ? v : null;
    }

    private static bool IsRemoteJob(GreenhouseJob job)
    {
        var text = $"{job.Location?.Name} {ResolveLocation(job)}".ToLowerInvariant();
        return text.Contains("remote") || text.Contains("anywhere");
    }

    /// <summary>
    /// Splits description on a "preferred" section marker, then extracts
    /// skill keywords from each part independently.
    /// </summary>
    private static (List<string> Required, List<string> Preferred) ExtractSkills(string plainText)
    {
        var markerMatch = PreferredSectionPattern.Match(plainText);
        string requiredPart, preferredPart;

        if (markerMatch.Success)
        {
            requiredPart = plainText[..markerMatch.Index];
            preferredPart = plainText[markerMatch.Index..];
        }
        else
        {
            requiredPart = plainText;
            preferredPart = string.Empty;
        }

        var required = ExtractKeywords(requiredPart);
        var preferred = ExtractKeywords(preferredPart);

        // A skill listed in both sections stays in required only
        preferred.RemoveAll(s => required.Contains(s, StringComparer.OrdinalIgnoreCase));

        return (required, preferred);
    }

    private static List<string> ExtractKeywords(string text)
    {
        var found = new List<string>();
        foreach (var kw in AllKeywords)
        {
            var escaped = Regex.Escape(kw);
            var pattern = $@"(?<![a-zA-Z0-9]){escaped}(?![a-zA-Z0-9])";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                found.Add(kw);
        }
        return found;
    }

    private static string StripHtml(string html)
    {
        // Decode entities FIRST — Greenhouse content may use &lt;h2&gt; style encoding.
        // Decoding after stripping would re-introduce tags into the plain text.
        var decoded = html
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&nbsp;", " ")
            .Replace("&#39;", "'")
            .Replace("&quot;", "\"");

        // Strip all HTML tags from the decoded string
        var text = HtmlTagPattern.Replace(decoded, " ");

        // Collapse whitespace
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string TruncatePlainText(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "...";

    // ---------------------------------------------------------------
    // JSON response models (private — not part of the domain)
    // ---------------------------------------------------------------

    private sealed class GreenhouseResponse
    {
        [JsonPropertyName("jobs")]
        public List<GreenhouseJob>? Jobs { get; init; }
    }

    private sealed class GreenhouseJob
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("location")]
        public GreenhouseLocation? Location { get; init; }

        [JsonPropertyName("departments")]
        public List<GreenhouseDepartment>? Departments { get; init; }

        [JsonPropertyName("metadata")]
        public List<GreenhouseMetadata>? Metadata { get; init; }

        [JsonPropertyName("absolute_url")]
        public string? AbsoluteUrl { get; init; }
    }

    private sealed class GreenhouseLocation
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class GreenhouseDepartment
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class GreenhouseMetadata
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        // value can be a string or array — deserialize as string for simplicity
        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }
}
