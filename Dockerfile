# ── Stage 1: build ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files and restore (cached layer unless .csproj changes)
COPY src/ResumeMatcher.Domain/ResumeMatcher.Domain.csproj             src/ResumeMatcher.Domain/
COPY src/ResumeMatcher.Application/ResumeMatcher.Application.csproj   src/ResumeMatcher.Application/
COPY src/ResumeMatcher.Infrastructure/ResumeMatcher.Infrastructure.csproj src/ResumeMatcher.Infrastructure/
COPY src/ResumeMatcher.Api/ResumeMatcher.Api.csproj                   src/ResumeMatcher.Api/
RUN dotnet restore src/ResumeMatcher.Api/ResumeMatcher.Api.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish src/ResumeMatcher.Api/ResumeMatcher.Api.csproj \
    -c Release -o /app/publish --no-restore

# ── Stage 2: runtime ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy published output
COPY --from=build /app/publish .

# ASP.NET Core listens on 8080 by default in containers
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "ResumeMatcher.Api.dll"]
