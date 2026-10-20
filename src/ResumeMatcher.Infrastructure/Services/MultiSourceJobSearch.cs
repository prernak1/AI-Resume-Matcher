using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;
using ResumeMatcher.Infrastructure.Repositories;
using ResumeMatcher.Infrastructure.Services.JobSources;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>
/// Searches every configured job source in parallel and merges the results:
///   - Greenhouse, Lever and Ashby company boards (from companies.json, or explicit tokens from the request)
///   - Adzuna keyword search (only when Adzuna:AppId / Adzuna:AppKey are configured)
/// Per-company results are cached briefly so repeated searches stay fast.
/// </summary>
public sealed class MultiSourceJobSearch : IJobSearchService
{
    private const int MaxParallelRequests = 8;

    private readonly GreenhouseJobRepository _greenhouse;
    private readonly LeverFeed _lever;
    private readonly AshbyFeed _ashby;
    private readonly AdzunaFeed? _adzuna;
    private readonly CompanyLists _configured;
    private readonly TimeSpan _cacheTtl;

    private readonly ConcurrentDictionary<string, (DateTime Expiry, IReadOnlyList<Job> Jobs)> _cache = new();
    private readonly SemaphoreSlim _throttle = new(MaxParallelRequests);

    public MultiSourceJobSearch(IHttpClientFactory httpFactory, IConfiguration configuration)
    {
        _greenhouse = new GreenhouseJobRepository(httpFactory, string.Empty);
        _lever = new LeverFeed(httpFactory);
        _ashby = new AshbyFeed(httpFactory);

        var appId = configuration["Adzuna:AppId"];
        var appKey = configuration["Adzuna:AppKey"];
        if (!string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(appKey))
            _adzuna = new AdzunaFeed(httpFactory, appId, appKey, configuration["Adzuna:Country"] is { Length: > 0 } c ? c : "in");

        _cacheTtl = TimeSpan.FromMinutes(int.TryParse(configuration["Greenhouse:CacheMinutes"], out var m) ? m : 10);
        _configured = LoadCompanies(configuration["Jobs:CompaniesFile"] is { Length: > 0 } f ? f : "companies.json");

        // A single default board configured the old way is still honoured
        if (configuration["Greenhouse:BoardToken"] is { Length: > 0 } defaultBoard &&
            !_configured.Greenhouse.Contains(defaultBoard, StringComparer.OrdinalIgnoreCase))
            _configured.Greenhouse.Add(defaultBoard);
    }

    public async Task<IReadOnlyList<Job>> SearchAsync(JobSearchRequest request, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<IReadOnlyList<Job>>>();

        if (request.Companies.Count > 0)
        {
            // An explicit token could live on any platform — try all three; wrong ones just return nothing
            foreach (var token in request.Companies)
            {
                tasks.Add(Cached("greenhouse", token, ct => _greenhouse.GetAllAsync(token, ct), cancellationToken));
                tasks.Add(Cached("lever", token, ct => _lever.FetchAsync(token, ct), cancellationToken));
                tasks.Add(Cached("ashby", token, ct => _ashby.FetchAsync(token, ct), cancellationToken));
            }
        }
        else
        {
            foreach (var t in _configured.Greenhouse)
                tasks.Add(Cached("greenhouse", t, ct => _greenhouse.GetAllAsync(t, ct), cancellationToken));
            foreach (var t in _configured.Lever)
                tasks.Add(Cached("lever", t, ct => _lever.FetchAsync(t, ct), cancellationToken));
            foreach (var t in _configured.Ashby)
                tasks.Add(Cached("ashby", t, ct => _ashby.FetchAsync(t, ct), cancellationToken));

            if (_adzuna is not null)
            {
                var roles = request.Roles.Count > 0 ? request.Roles.Take(3) : ["software engineer"];
                foreach (var role in roles)
                    tasks.Add(Throttled(ct => _adzuna.SearchAsync(role, request.PostedWithinDays, ct), cancellationToken));
            }
        }

        var results = await Task.WhenAll(tasks);
        IEnumerable<Job> jobs = results.SelectMany(r => r);

        if (request.PostedWithinDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-request.PostedWithinDays);
            jobs = jobs.Where(j => j.PostedAt is { } p && p >= cutoff);
        }

        return jobs
            .Where(j => !string.IsNullOrEmpty(j.ApplyUrl))
            .Where(j => LocationFilter.Matches(j, request.Location))
            .GroupBy(j => j.ApplyUrl!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList()
            .AsReadOnly();
    }

    private async Task<IReadOnlyList<Job>> Cached(
        string platform, string token, Func<CancellationToken, Task<IReadOnlyList<Job>>> fetch, CancellationToken ct)
    {
        var key = $"{platform}:{token.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out var hit) && hit.Expiry > DateTime.UtcNow)
            return hit.Jobs;

        var jobs = await Throttled(fetch, ct);
        if (jobs.Count > 0)
            _cache[key] = (DateTime.UtcNow + _cacheTtl, jobs);
        return jobs;
    }

    private async Task<IReadOnlyList<Job>> Throttled(Func<CancellationToken, Task<IReadOnlyList<Job>>> fetch, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            return await fetch(ct);
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static CompanyLists LoadCompanies(string file)
    {
        var path = Path.IsPathRooted(file) ? file : Path.Combine(AppContext.BaseDirectory, file);
        try
        {
            if (File.Exists(path))
            {
                var lists = JsonSerializer.Deserialize<CompanyLists>(
                    File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (lists is not null) return lists;
            }
        }
        catch
        {
            // Malformed file — fall through to an empty list rather than failing startup
        }
        return new CompanyLists();
    }

    private sealed class CompanyLists
    {
        public List<string> Greenhouse { get; init; } = [];
        public List<string> Lever { get; init; } = [];
        public List<string> Ashby { get; init; } = [];
    }
}
