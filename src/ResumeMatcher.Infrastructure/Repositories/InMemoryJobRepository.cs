using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Infrastructure.Repositories;

/// <summary>
/// Step 1: Returns hardcoded sample jobs.
///
/// WHY HARDCODED FOR NOW:
/// We are building incrementally. In Step 1, the goal is to run the application
/// and prove the architecture works — not to set up a database.
/// In Step 3, we will replace this with a real PostgreSQL implementation
/// without changing a single line of Application code.
///
/// This is the Open/Closed Principle: the Application is CLOSED for modification
/// but OPEN for extension (we add a new class, we don't change existing ones).
/// </summary>
public sealed class InMemoryJobRepository : IJobRepository
{
    private static readonly IReadOnlyList<Job> _jobs =
    [
        Job.Create(
            title: "Backend Engineer",
            description: "Build and maintain scalable backend services using .NET.",
            requiredSkills: ["C#", "ASP.NET Core", "SQL", "REST API"],
            preferredSkills: ["Docker", "Kubernetes", "Redis", "Azure"]),

        Job.Create(
            title: ".NET Developer",
            description: "Develop enterprise applications on the Microsoft stack.",
            requiredSkills: ["C#", ".NET", "SQL Server", "Entity Framework"],
            preferredSkills: ["Azure", "WPF", "MAUI", "Blazor"]),

        Job.Create(
            title: "Software Engineer",
            description: "General software engineering role across the full stack.",
            requiredSkills: ["Programming", "Problem Solving", "Git", "Agile"],
            preferredSkills: ["C#", "Python", "JavaScript", "Cloud"]),

        Job.Create(
            title: "Full Stack Developer",
            description: "Build both frontend and backend systems.",
            requiredSkills: ["C#", "ASP.NET Core", "React", "SQL"],
            preferredSkills: ["TypeScript", "Docker", "REST API", "Azure"]),

        Job.Create(
            title: "Cloud Engineer",
            description: "Design and operate cloud infrastructure on Azure or AWS.",
            requiredSkills: ["Azure", "Terraform", "CI/CD", "Linux"],
            preferredSkills: ["Kubernetes", "Docker", "Python", "Networking"]),

        Job.Create(
            title: "DevOps Engineer",
            description: "Automate deployments and manage infrastructure as code.",
            requiredSkills: ["CI/CD", "Docker", "Kubernetes", "Azure DevOps"],
            preferredSkills: ["Terraform", "Python", "Linux", "Monitoring"]),

        Job.Create(
            title: "AI/ML Engineer",
            description: "Build AI-powered features using OpenAI and ML frameworks.",
            requiredSkills: ["Python", "OpenAI API", "Machine Learning", "REST API"],
            preferredSkills: ["C#", "Vector Databases", "LangChain", "Azure"])
    ];

    public Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_jobs);
}
