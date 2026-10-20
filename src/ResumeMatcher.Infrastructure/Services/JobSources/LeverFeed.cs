using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using ResumeMatcher.Domain.Entities;
using ResumeMatcher.Infrastructure.Repositories;

namespace ResumeMatcher.Infrastructure.Services.JobSources;

/// <summary>Lever public postings API: GET https://api.lever.co/v0/postings/{company}?mode=json</summary>
internal sealed class LeverFeed
{
    private readonly IHttpClientFactory _http;
    public LeverFeed(IHttpClientFactory http) => _http = http;

    public async Task<IReadOnlyList<Job>> FetchAsync(string company, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("JobFeeds");
            var postings = await client.GetFromJsonAsync<List<LeverPosting>>(
                $"https://api.lever.co/v0/postings/{Uri.EscapeDataString(company)}?mode=json", ct);
            if (postings is null) return [];

            return postings
                .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                .Select(p => Map(p, company))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static Job Map(LeverPosting p, string company)
    {
        var sb = new StringBuilder(p.DescriptionPlain);
        foreach (var l in p.Lists ?? [])
            sb.Append(' ').Append(l.Text).Append(": ").Append(GreenhouseJobRepository.StripHtml(l.Content ?? string.Empty));
        sb.Append(' ').Append(p.AdditionalPlain);

        var plain = GreenhouseJobRepository.StripHtml(sb.ToString());
        var (required, preferred) = GreenhouseJobRepository.ExtractSkills(plain);

        var location = p.Categories?.Location ?? string.Empty;
        var allLocations = string.Join("; ",
            new[] { location }.Concat(p.Categories?.AllLocations ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct());
        var desc = GreenhouseJobRepository.TruncatePlainText(plain, 300);
        if (!string.IsNullOrEmpty(location)) desc = $"[{location}] {desc}";
        if (!string.IsNullOrEmpty(p.Categories?.Team)) desc = $"({p.Categories!.Team}) {desc}";

        return Job.Create(
            title: p.Text!,
            description: desc,
            requiredSkills: required,
            preferredSkills: preferred,
            applyUrl: p.ApplyUrl ?? p.HostedUrl,
            source: company,
            postedAt: p.CreatedAt is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null,
            fullDescription: $"{p.Text}. {plain}",
            platform: "lever",
            location: allLocations);
    }

    private sealed class LeverPosting
    {
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("hostedUrl")] public string? HostedUrl { get; init; }
        [JsonPropertyName("applyUrl")] public string? ApplyUrl { get; init; }
        [JsonPropertyName("createdAt")] public long? CreatedAt { get; init; }
        [JsonPropertyName("descriptionPlain")] public string? DescriptionPlain { get; init; }
        [JsonPropertyName("additionalPlain")] public string? AdditionalPlain { get; init; }
        [JsonPropertyName("categories")] public LeverCategories? Categories { get; init; }
        [JsonPropertyName("lists")] public List<LeverList>? Lists { get; init; }
    }

    private sealed class LeverCategories
    {
        [JsonPropertyName("location")] public string? Location { get; init; }
        [JsonPropertyName("team")] public string? Team { get; init; }
        [JsonPropertyName("allLocations")] public List<string>? AllLocations { get; init; }
    }

    private sealed class LeverList
    {
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
}
