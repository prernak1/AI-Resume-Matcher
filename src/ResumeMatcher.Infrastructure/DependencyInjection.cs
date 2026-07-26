using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Infrastructure.Repositories;
using ResumeMatcher.Infrastructure.Services;

namespace ResumeMatcher.Infrastructure;

/// <summary>
/// Registers Infrastructure layer services with the DI container.
/// Called from API's Program.cs: builder.Services.AddInfrastructure(builder.Configuration)
///
/// Job source strategy:
///   - Greenhouse:BoardToken is set  → GreenhouseJobRepository (live API, 10-min cache)
///   - Not set                       → InMemoryJobRepository   (hardcoded sample jobs)
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Named HttpClient for Greenhouse — re-uses socket pool, sets base address + timeout
        services.AddHttpClient("Greenhouse", client =>
        {
            client.BaseAddress = new Uri("https://boards-api.greenhouse.io/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Job repository — live Greenhouse data if configured, otherwise in-memory samples
        var boardToken = configuration["Greenhouse:BoardToken"] ?? string.Empty;
        var cacheMinutes = int.TryParse(configuration["Greenhouse:CacheMinutes"], out var cm) ? cm : 10;

        if (!string.IsNullOrWhiteSpace(boardToken))
        {
            services.AddSingleton<IJobRepository>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new GreenhouseJobRepository(factory, boardToken, cacheMinutes);
            });
        }
        else
        {
            services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        }

        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<IResumeAnalyzer, KeywordResumeAnalyzer>();
        services.AddSingleton<IJobMatcher, SkillBasedJobMatcher>();

        return services;
    }
}
