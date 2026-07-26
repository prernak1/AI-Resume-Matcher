using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

/// <summary>
/// Defines the contract for AI-powered resume analysis.
///
/// WHY AN INTERFACE HERE (not the implementation):
/// Application defines WHAT it needs. Infrastructure provides HOW.
/// Tomorrow you could swap OpenAI for Claude, Gemini, or a local model
/// by creating a new class that implements this interface — no other code changes.
///
/// This is the Dependency Inversion Principle (the D in SOLID).
/// High-level modules (Application) should not depend on low-level modules (Infrastructure).
/// Both should depend on abstractions (this interface).
/// </summary>
public interface IResumeAnalyzer
{
    /// <summary>
    /// Sends the extracted resume text to OpenAI and returns structured analysis.
    /// </summary>
    Task<ResumeAnalysis> AnalyzeAsync(string resumeText, CancellationToken cancellationToken = default);
}
