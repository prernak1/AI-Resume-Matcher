using Microsoft.Extensions.DependencyInjection;

namespace ResumeMatcher.Application;

/// <summary>
/// Registers Application layer services with the DI container.
///
/// Called from API's Program.cs: builder.Services.AddApplication()
///
/// WHY A STATIC EXTENSION METHOD:
/// Keeps Program.cs clean — it says "add application services" without
/// knowing what those services are. Each layer registers its own dependencies.
///
/// In Step 1, there is nothing to register here yet.
/// In Step 2, we will register MediatR and FluentValidation here.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Step 2: MediatR + FluentValidation will be registered here.
        return services;
    }
}
