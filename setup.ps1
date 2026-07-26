#!/usr/bin/env pwsh
# =============================================================
# AI Resume Job Matcher — Step 1 Setup Script
# Run once to create the solution and project structure.
# Prerequisite: .NET 9 SDK
# =============================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Creating AI Resume Job Matcher solution..." -ForegroundColor Cyan

# ---- Solution -------------------------------------------------------
dotnet new sln -n ResumeMatcher

# ---- Projects -------------------------------------------------------
dotnet new classlib -n ResumeMatcher.Domain        -o src/ResumeMatcher.Domain        --framework net9.0
dotnet new classlib -n ResumeMatcher.Application   -o src/ResumeMatcher.Application   --framework net9.0
dotnet new classlib -n ResumeMatcher.Infrastructure -o src/ResumeMatcher.Infrastructure --framework net9.0
dotnet new webapi   -n ResumeMatcher.Api           -o src/ResumeMatcher.Api           --framework net9.0 --no-openapi

# Remove template boilerplate
Remove-Item -Path "src/ResumeMatcher.Domain/Class1.cs"        -ErrorAction SilentlyContinue
Remove-Item -Path "src/ResumeMatcher.Application/Class1.cs"   -ErrorAction SilentlyContinue
Remove-Item -Path "src/ResumeMatcher.Infrastructure/Class1.cs" -ErrorAction SilentlyContinue

# ---- Add to solution ------------------------------------------------
dotnet sln add src/ResumeMatcher.Domain/ResumeMatcher.Domain.csproj
dotnet sln add src/ResumeMatcher.Application/ResumeMatcher.Application.csproj
dotnet sln add src/ResumeMatcher.Infrastructure/ResumeMatcher.Infrastructure.csproj
dotnet sln add src/ResumeMatcher.Api/ResumeMatcher.Api.csproj

# ---- Project references (dependencies point INWARD) -----------------
# Application depends on Domain
dotnet add src/ResumeMatcher.Application/ResumeMatcher.Application.csproj `
    reference src/ResumeMatcher.Domain/ResumeMatcher.Domain.csproj

# Infrastructure depends on Application (and Domain transitively)
dotnet add src/ResumeMatcher.Infrastructure/ResumeMatcher.Infrastructure.csproj `
    reference src/ResumeMatcher.Application/ResumeMatcher.Application.csproj

# Api depends on Application + Infrastructure
dotnet add src/ResumeMatcher.Api/ResumeMatcher.Api.csproj `
    reference src/ResumeMatcher.Application/ResumeMatcher.Application.csproj
dotnet add src/ResumeMatcher.Api/ResumeMatcher.Api.csproj `
    reference src/ResumeMatcher.Infrastructure/ResumeMatcher.Infrastructure.csproj

# ---- NuGet packages (Step 1 only — minimal) -------------------------
dotnet add src/ResumeMatcher.Api/ResumeMatcher.Api.csproj `
    package Swashbuckle.AspNetCore --version 7.2.0

# ---- Restore --------------------------------------------------------
dotnet restore

Write-Host ""
Write-Host "Done! Now copy the source files, then run:" -ForegroundColor Green
Write-Host "  dotnet run --project src/ResumeMatcher.Api" -ForegroundColor White
Write-Host "  Open: http://localhost:5000/swagger" -ForegroundColor White
