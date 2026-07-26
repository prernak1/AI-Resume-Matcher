using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;
using System.Text.RegularExpressions;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>
/// Keyword-based resume analyzer. Scans resume text for known technologies,
/// skills, and patterns to produce a structured ResumeAnalysis.
/// No external API key required — runs entirely locally.
/// </summary>
public sealed class KeywordResumeAnalyzer : IResumeAnalyzer
{
    // Ordered so longer/more-specific tokens are checked first to avoid
    // partial matches (e.g. "ASP.NET Core" before ".NET").
    private static readonly string[] KnownSkills =
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
        "Go", "Rust", "Kotlin", "Swift",
        "REST API", "REST", "GraphQL", "gRPC", "SignalR",
        "RabbitMQ", "Kafka", "Redis",
        "MongoDB", "Elasticsearch",
        "Microservices", "CQRS", "DDD", "Event Sourcing",
        "MediatR", "AutoMapper", "Polly",
        "xUnit", "NUnit", "MSTest", "Moq",
        "Selenium", "Playwright", "Cypress",
        "Jenkins", "TeamCity", "CircleCI",
        "CI/CD", "DevOps",
        "Linux", "Bash", "PowerShell", "Windows",
        "Nginx", "IIS",
        "HTML", "CSS", "SASS",
        "WPF", "MAUI", "WinForms",
        "Machine Learning", "ML", "OpenAI", "LLM",
        "Agile", "Scrum", "Kanban", "JIRA",
        "TDD", "BDD", "Unit Testing", "Integration Testing",
        "SOLID", "Design Patterns", "OOP",
        "Networking", "TCP/IP", "HTTP", "HTTPS",
        "API Gateway", "Load Balancing",
        "Monitoring", "Logging", "Serilog", "NLog",
        "LINQ", "EF Core", "Dapper",
        "OAuth", "JWT", "OpenID Connect", "Identity",
        "SQL", "NoSQL",
    ];

    private static readonly string[] TechCategories =
    [
        "Docker", "Kubernetes", "Azure", "AWS", "GCP",
        "PostgreSQL", "SQL Server", "MongoDB", "Redis", "Elasticsearch",
        "RabbitMQ", "Kafka", "Nginx", "IIS",
        "GitHub Actions", "Jenkins", "TeamCity",
        "Terraform", "Ansible", "Helm",
    ];

    private static readonly string[] BackendRoles =
        ["Backend Developer", "Backend Engineer", "Software Engineer", ".NET Developer", "Full Stack Developer"];
    private static readonly string[] CloudRoles =
        ["Cloud Engineer", "DevOps Engineer", "Platform Engineer", "SRE"];
    private static readonly string[] AIRoles =
        ["AI/ML Engineer", "ML Engineer", "Data Scientist"];

    public Task<ResumeAnalysis> AnalyzeAsync(string resumeText, CancellationToken cancellationToken = default)
    {
        var text = resumeText;
        var skills = ExtractMatches(text, KnownSkills);
        var technologies = skills.Where(s => TechCategories.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        var yearsOfExperience = EstimateYearsOfExperience(text);
        var education = ExtractEducation(text);
        var suggestedRoles = SuggestRoles(skills);
        var summary = BuildSummary(skills, yearsOfExperience, education);

        var analysis = new ResumeAnalysis
        {
            Skills = skills,
            Technologies = technologies,
            Education = education,
            YearsOfExperience = yearsOfExperience,
            SuggestedRoles = suggestedRoles,
            Summary = summary
        };

        return Task.FromResult(analysis);
    }

    private static List<string> ExtractMatches(string text, string[] candidates)
    {
        var found = new List<string>();
        foreach (var candidate in candidates)
        {
            // Word-boundary aware match; handle special chars like C#, .NET, etc.
            var escaped = Regex.Escape(candidate);
            var pattern = $@"(?<![a-zA-Z0-9]){escaped}(?![a-zA-Z0-9])";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                found.Add(candidate);
            }
        }
        return found;
    }

    private static int EstimateYearsOfExperience(string text)
    {
        // Look for explicit "X years of experience" / "X+ years" patterns
        var explicit_ = Regex.Match(text, @"(\d+)\+?\s*years?\s*(of\s*)?experience", RegexOptions.IgnoreCase);
        if (explicit_.Success && int.TryParse(explicit_.Groups[1].Value, out var years))
            return years;

        // Fall back: count distinct year tokens and infer span
        var yearMatches = Regex.Matches(text, @"\b(20[0-2]\d)\b");
        var yearSet = yearMatches.Cast<Match>()
                                  .Select(m => int.Parse(m.Value))
                                  .Where(y => y >= 2010 && y <= DateTime.UtcNow.Year)
                                  .ToHashSet();

        if (yearSet.Count >= 2)
            return DateTime.UtcNow.Year - yearSet.Min();

        return 0;
    }

    private static string ExtractEducation(string text)
    {
        // Check for degree keywords
        foreach (var (pattern, label) in new (string, string)[]
        {
            (@"\bPh\.?D\.?\b", "PhD"),
            (@"\bM\.?S\.?\b|\bMaster'?s?\b|\bMSc\b|\bM\.?Tech\b", "Master's Degree"),
            (@"\bB\.?S\.?\b|\bBachelor'?s?\b|\bBSc\b|\bB\.?Tech\b|\bBE\b|\bB\.?E\.?\b", "Bachelor's Degree"),
            (@"\bDiploma\b", "Diploma"),
        })
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                return label;
        }
        return "Not specified";
    }

    private static List<string> SuggestRoles(List<string> skills)
    {
        var roles = new List<string>();
        var skillSet = new HashSet<string>(skills, StringComparer.OrdinalIgnoreCase);

        bool hasDotNet = skillSet.Contains("C#") || skillSet.Contains(".NET") || skillSet.Contains("ASP.NET Core");
        bool hasCloud = skillSet.Contains("Azure") || skillSet.Contains("AWS") || skillSet.Contains("GCP");
        bool hasDevOps = skillSet.Contains("Docker") || skillSet.Contains("Kubernetes") || skillSet.Contains("CI/CD");
        bool hasAI = skillSet.Contains("Machine Learning") || skillSet.Contains("ML") || skillSet.Contains("OpenAI");
        bool hasFrontend = skillSet.Contains("React") || skillSet.Contains("Angular") || skillSet.Contains("Vue");

        if (hasDotNet) roles.AddRange(BackendRoles);
        if (hasCloud && hasDevOps) roles.AddRange(CloudRoles);
        if (hasFrontend && hasDotNet) roles.Add("Full Stack Developer");
        if (hasAI) roles.AddRange(AIRoles);
        if (!roles.Any()) roles.Add("Software Engineer");

        return roles.Distinct().Take(5).ToList();
    }

    private static string BuildSummary(List<string> skills, int years, string education)
    {
        var yearsText = years > 0 ? $"{years}+ years of experience" : "experience";
        var topSkills = string.Join(", ", skills.Take(6));
        return $"Candidate with {yearsText} ({education}). Key skills: {topSkills}.";
    }
}
