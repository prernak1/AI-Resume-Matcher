using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

public interface IProfileExtractor
{
    CandidateProfile Extract(string resumeText);
}
