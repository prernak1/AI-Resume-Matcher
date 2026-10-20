using ResumeMatcher.Application;
using ResumeMatcher.Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Optional git-ignored file for local secrets (e.g. the AI API key)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ---------------------------------------------------------------
// 1. Register services from each layer
// ---------------------------------------------------------------

// Application layer (use cases, validators — empty in Step 1)
builder.Services.AddApplication();

// Infrastructure layer (repositories, AI services, PDF parser)
builder.Services.AddInfrastructure(builder.Configuration);

// ---------------------------------------------------------------
// 2. ASP.NET Core services
// ---------------------------------------------------------------
builder.Services.AddControllers();

// ---------------------------------------------------------------
// 3. Swagger / OpenAPI
//    Accessible at http://localhost:5000/swagger
//    This is your interactive API documentation.
// ---------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AI Resume Job Matcher",
        Version = "v1",
        Description = "Upload a resume, extract skills with AI, match against jobs."
    });
});

// ---------------------------------------------------------------
// 4. Health checks
//    Accessible at http://localhost:5000/health
//    Used by Docker, Kubernetes, and load balancers to verify
//    that the application is running and ready to serve traffic.
// ---------------------------------------------------------------
builder.Services.AddHealthChecks();

// ---------------------------------------------------------------
// Build
// ---------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------
// 5. Middleware pipeline
//    ORDER MATTERS — middleware runs top to bottom on each request.
// ---------------------------------------------------------------

// Always show Swagger (in production you'd gate this behind auth)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "AI Resume Job Matcher v1");
    options.DisplayRequestDuration(); // Shows how long each request takes
});

// Serve wwwroot/index.html at "/" (drag-and-drop resume UI)
app.UseDefaultFiles();   // maps "/" → "/index.html"
app.UseStaticFiles();    // serves files from wwwroot/

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
