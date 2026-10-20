using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ResumeMatcher.Domain.Entities;
using ResumeMatcher.Infrastructure.Repositories;

namespace ResumeMatcher.Infrastructure.Services.JobSources;

/// <summary>Ashby public job board API: GET https://api.ashbyhq.com/posting-api/job-board/{company}</summary>
internal sealed class AshbyFeed
{
    private readonly IHttpClientFactory _http;
    public AshbyFeed(IHttpClientFactory http) => _http = http;

    public async Task<IReadOnlyList<Job>> FetchAsync(string company, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("JobFeeds");
            var board = await client.GetFromJsonAsync<AshbyBoard>(
                $"https://api.ashbyhq.com/posting-api/job-board/{Uri.EscapeDataString(company)}", ct);
            if (board?.Jobs is null) return [];

            return board.Jobs
                .Where(j => j.IsListed != false && !string.IsNullOrWhiteSpace(j.Title))
                .Select(j => Map(j, company))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static Job Map(AshbyJob j, string company)
    {
        var plain = GreenhouseJobRepository.StripHtml(j.DescriptionPlain ?? string.Empty);
        var (required, preferred) = GreenhouseJobRepository.ExtractSkills(plain);

        var location = j.Location ?? string.Empty;
        var allLocations = string.Join("; ",
            new[] { location }.Concat((j.SecondaryLocations ?? []).Select(s => s.Location ?? string.Empty))
                .Where(l => !string.IsNullOrWhiteSpace(l)).Distinct());
        if (j.IsRemote == true && !location.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            location = string.IsNullOrEmpty(location) ? "Remote" : $"{location} · Remote";

        var desc = GreenhouseJobRepository.TruncatePlainText(plain, 300);
        if (!string.IsNullOrEmpty(location)) desc = $"[{location}] {desc}";
        if (!string.IsNullOrEmpty(j.Department)) desc = $"({j.Department}) {desc}";

        return Job.Create(
            title: j.Title!,
            description: desc,
            requiredSkills: required,
            preferredSkills: preferred,
            applyUrl: j.ApplyUrl ?? j.JobUrl,
            source: company,
            postedAt: j.PublishedAt,
            fullDescription: $"{j.Title}. {plain}",
            platform: "ashby",
            location: allLocations);
    }

    private sealed class AshbyBoard
    {
        [JsonPropertyName("jobs")] public List<AshbyJob>? Jobs { get; init; }
    }

    private sealed class AshbyLocation
    {
        [JsonPropertyName("location")] public string? Location { get; init; }
    }

    private sealed class AshbyJob
    {
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("location")] public string? Location { get; init; }
        [JsonPropertyName("department")] public string? Department { get; init; }
        [JsonPropertyName("isRemote")] public bool? IsRemote { get; init; }
        [JsonPropertyName("isListed")] public bool? IsListed { get; init; }
        [JsonPropertyName("secondaryLocations")] public List<AshbyLocation>? SecondaryLocations { get; init; }
        [JsonPropertyName("publishedAt")] public DateTimeOffset? PublishedAt { get; init; }
        [JsonPropertyName("jobUrl")] public string? JobUrl { get; init; }
        [JsonPropertyName("applyUrl")] public string? ApplyUrl { get; init; }
        [JsonPropertyName("descriptionPlain")] public string? DescriptionPlain { get; init; }
    }
}
