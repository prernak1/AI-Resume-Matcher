using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ResumeMatcher.Domain.Entities;
using ResumeMatcher.Infrastructure.Repositories;

namespace ResumeMatcher.Infrastructure.Services.JobSources;

/// <summary>
/// Adzuna job search API (aggregates many job boards). Requires a free key from developer.adzuna.com.
/// Adzuna only returns a short description snippet and a redirect link to the original posting.
/// </summary>
internal sealed class AdzunaFeed
{
    private readonly IHttpClientFactory _http;
    private readonly string _appId;
    private readonly string _appKey;
    private readonly string _country;

    public AdzunaFeed(IHttpClientFactory http, string appId, string appKey, string country)
    {
        _http = http;
        _appId = appId;
        _appKey = appKey;
        _country = country;
    }

    public async Task<IReadOnlyList<Job>> SearchAsync(string what, int maxDaysOld, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.adzuna.com/v1/api/jobs/{Uri.EscapeDataString(_country)}/search/1" +
                      $"?app_id={Uri.EscapeDataString(_appId)}&app_key={Uri.EscapeDataString(_appKey)}" +
                      $"&results_per_page=50&sort_by=date&what={Uri.EscapeDataString(what)}" +
                      (maxDaysOld > 0 ? $"&max_days_old={maxDaysOld}" : string.Empty);

            var client = _http.CreateClient("JobFeeds");
            var response = await client.GetFromJsonAsync<AdzunaResponse>(url, ct);
            if (response?.Results is null) return [];

            return response.Results
                .Where(r => !string.IsNullOrWhiteSpace(r.Title) && !string.IsNullOrWhiteSpace(r.RedirectUrl))
                .Select(r => Map(r, _country))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static Job Map(AdzunaResult r, string country)
    {
        var title = GreenhouseJobRepository.StripHtml(r.Title!);
        var plain = GreenhouseJobRepository.StripHtml(r.Description ?? string.Empty);
        var (required, preferred) = GreenhouseJobRepository.ExtractSkills($"{title}. {plain}");

        var desc = GreenhouseJobRepository.TruncatePlainText(plain, 300);
        if (!string.IsNullOrEmpty(r.Location?.DisplayName)) desc = $"[{r.Location!.DisplayName}] {desc}";

        return Job.Create(
            title: title,
            description: desc,
            requiredSkills: required,
            preferredSkills: preferred,
            applyUrl: r.RedirectUrl,
            source: r.Company?.DisplayName ?? "adzuna",
            postedAt: r.Created,
            fullDescription: $"{title}. {plain}",
            platform: "adzuna",
            location: AdzunaLocation(r, country));
    }

    // Adzuna scopes results to one country but often names only a city or state — keep the country in the text
    private static string AdzunaLocation(AdzunaResult r, string country)
    {
        var name = r.Location?.DisplayName ?? string.Empty;
        return country.Equals("in", StringComparison.OrdinalIgnoreCase) && !name.Contains("India", StringComparison.OrdinalIgnoreCase)
            ? (name.Length > 0 ? $"{name}, India" : "India")
            : name;
    }

    private sealed class AdzunaResponse
    {
        [JsonPropertyName("results")] public List<AdzunaResult>? Results { get; init; }
    }

    private sealed class AdzunaResult
    {
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("redirect_url")] public string? RedirectUrl { get; init; }
        [JsonPropertyName("created")] public DateTimeOffset? Created { get; init; }
        [JsonPropertyName("company")] public AdzunaName? Company { get; init; }
        [JsonPropertyName("location")] public AdzunaName? Location { get; init; }
    }

    private sealed class AdzunaName
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
    }
}
