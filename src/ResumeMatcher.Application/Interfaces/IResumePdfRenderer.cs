using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Application.Interfaces;

public interface IResumePdfRenderer
{
    byte[] Render(TailoredResume resume);
}
