using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ResumeMatcher.Application.Interfaces;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>JSON-file backed log of applications (default: data/applications.json next to the app).</summary>
public sealed class JsonFileApplicationLog : IApplicationLog
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<ApplicationRecord> _records;

    public JsonFileApplicationLog(IConfiguration configuration)
    {
        var dir = configuration["Apply:DataDirectory"] is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumeMatcher");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "applications.json");

        _records = [];
        try
        {
            if (File.Exists(_path))
                _records = JsonSerializer.Deserialize<List<ApplicationRecord>>(File.ReadAllText(_path)) ?? [];
        }
        catch
        {
            // Corrupt log — start fresh rather than blocking the app
        }
    }

    public bool HasApplied(string applyUrl)
    {
        lock (_gate)
            return _records.Any(r => string.Equals(r.ApplyUrl, applyUrl, StringComparison.OrdinalIgnoreCase)
                                     && r.Status is "Submitted" or "Submitted (unconfirmed)");
    }

    public void Record(ApplicationRecord record)
    {
        lock (_gate)
        {
            _records.Add(record);
            File.WriteAllText(_path, JsonSerializer.Serialize(_records, Options));
        }
    }

    public IReadOnlyList<ApplicationRecord> GetAll()
    {
        lock (_gate) return _records.OrderByDescending(r => r.At).ToList();
    }
}
