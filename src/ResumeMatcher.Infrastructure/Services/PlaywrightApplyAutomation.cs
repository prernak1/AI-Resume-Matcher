using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using ResumeMatcher.Application.Interfaces;
using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>
/// Drives a real Chromium window with Playwright: opens the application page, fills the standard fields
/// from the candidate profile, uploads the resume, then leaves the window open.
/// The form is only submitted when <see cref="SubmitAsync"/> is called (the user's explicit approval).
///
/// Fields are matched by their visible label / placeholder / name, so it works across Greenhouse,
/// Lever, Ashby and most plain HTML forms. Custom questions, dropdowns, CAPTCHAs and email verification
/// are NOT answered — they are reported back so the user can finish them in the open window.
/// </summary>
public sealed class PlaywrightApplyAutomation : IApplyAutomation, IAsyncDisposable
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(45);

    private static readonly Regex SubmitButtonName = new(
        @"^\s*(submit( application)?|apply( now)?|send application)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ApplyOpenerName = new(
        @"^\s*apply( for this job| now| here)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SuccessText = new(
        @"thank you for (applying|your application)|application (has been |was )?(submitted|received)|successfully (submitted|applied)|we('ve| have) received your application",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Order matters: specific labels are tested before generic ones ("first name" before "name").
    private static readonly (Regex Label, Func<CandidateProfile, string> Value, string Display)[] FieldRules =
    [
        (new(@"first\s*name|given\s*name|\bfname\b", RegexOptions.IgnoreCase), p => p.FirstName, "First name"),
        (new(@"last\s*name|family\s*name|surname|\blname\b", RegexOptions.IgnoreCase), p => p.LastName, "Last name"),
        (new(@"e-?mail", RegexOptions.IgnoreCase), p => p.Email, "Email"),
        (new(@"phone|mobile|contact\s*number", RegexOptions.IgnoreCase), p => p.Phone, "Phone"),
        (new(@"linked\s*in", RegexOptions.IgnoreCase), p => p.LinkedIn, "LinkedIn"),
        (new(@"git\s*hub", RegexOptions.IgnoreCase), p => p.GitHub, "GitHub"),
        (new(@"website|portfolio|personal\s*(site|url)", RegexOptions.IgnoreCase), p => p.Website, "Website"),
        (new(@"current\s*location|\bcity\b|where are you (located|based)|^\s*location", RegexOptions.IgnoreCase), p => p.Location, "Location"),
        (new(@"^\s*(full\s*|legal\s*|your\s*)?name\b", RegexOptions.IgnoreCase), p => p.FullName, "Full name"),
    ];

    private static readonly Regex NeverFill = new(@"company|employer|organi[sz]ation|school|university|cover\s*letter|salary|pronoun",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string ScanFieldsScript = """
        () => {
          const out = [];
          let i = 0;
          const labelOf = el => {
            const parts = [];
            const aria = el.getAttribute('aria-label'); if (aria) parts.push(aria);
            const by = el.getAttribute('aria-labelledby');
            if (by) by.split(/\s+/).forEach(id => { const n = document.getElementById(id); if (n) parts.push(n.innerText); });
            if (el.labels) Array.from(el.labels).forEach(l => parts.push(l.innerText));
            const wrap = el.closest('label'); if (wrap) parts.push(wrap.innerText);
            if (el.placeholder) parts.push(el.placeholder);
            if (!parts.length) { if (el.name) parts.push(el.name); if (el.id) parts.push(el.id); }
            return parts.join(' ').replace(/\s+/g, ' ').trim();
          };
          document.querySelectorAll('input, textarea, select').forEach(el => {
            const type = (el.type || el.tagName).toLowerCase();
            if (['hidden', 'submit', 'button', 'image', 'reset'].includes(type)) return;
            const visible = type === 'file' || (el.offsetParent !== null && getComputedStyle(el).visibility !== 'hidden');
            if (!visible) return;
            el.setAttribute('data-rm-idx', String(i));
            const label = labelOf(el);
            const isBox = type === 'checkbox' || type === 'radio';
            out.push({
              idx: i,
              tag: el.tagName.toLowerCase(),
              type: type,
              label: label,
              name: el.name || '',
              id: el.id || '',
              required: !!el.required || el.getAttribute('aria-required') === 'true' || /\*\s*$/.test(label),
              filled: type === 'file' ? (el.files && el.files.length > 0) : isBox ? el.checked : (el.value || '').trim().length > 0,
              combo: el.getAttribute('role') === 'combobox'
            });
            i++;
          });
          return out;
        }
        """;

    private readonly bool _headless;
    private readonly string[] _channels;
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightApplyAutomation(IConfiguration configuration)
    {
        _headless = bool.TryParse(configuration["Apply:Headless"], out var h) && h;
        _channels = configuration["Apply:BrowserChannel"] is { Length: > 0 } c
            ? [c]
            : ["chrome", "msedge"];
    }

    public async Task<ApplyPrepareResult> PrepareAsync(
        string applyUrl,
        CandidateProfile profile,
        byte[] resumePdf,
        string resumeFileName,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(applyUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("The job has no valid http(s) apply link.");

        PurgeExpiredSessions();
        var browser = await GetBrowserAsync();

        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = null });
        var page = await context.NewPageAsync();
        var sessionId = Guid.NewGuid().ToString("N");

        try
        {
            await page.GotoAsync(applyUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 8_000 })
                .ContinueWith(_ => { }, CancellationToken.None);

            await OpenFormIfNeededAsync(page);

            var fields = await ScanFieldsAsync(page);
            var filled = new List<string>();
            await FillTextFieldsAsync(page, fields, profile, filled);
            var uploaded = await UploadResumeAsync(page, fields, resumePdf, resumeFileName);
            if (uploaded) filled.Add("Resume (PDF)");

            // Re-scan so "missing" reflects the state after filling
            var after = await ScanFieldsAsync(page);
            var missing = after
                .Where(f => f.Required && !f.Filled)
                .Select(f => Describe(f))
                .Distinct()
                .ToList();

            _sessions[sessionId] = new Session(context, page, DateTime.UtcNow);
            return new ApplyPrepareResult(sessionId, filled, missing, uploaded);
        }
        catch (PlaywrightException ex)
        {
            await context.CloseAsync();
            throw new InvalidOperationException(FirstLine(ex.Message), ex);
        }
        catch
        {
            await context.CloseAsync();
            throw;
        }
    }

    public async Task<ApplySubmitResult> SubmitAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new ApplySubmitResult("Expired", false, "This application session no longer exists. Prepare it again.");

        try
        {
            var page = session.Page;
            var urlBefore = page.Url;

            var button = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameRegex = SubmitButtonName }).First;
            if (await button.CountAsync() == 0)
                button = page.Locator("button[type=submit], input[type=submit]").First;
            if (await button.CountAsync() == 0)
                return new ApplySubmitResult("NoSubmitButton", false, "Couldn't find a Submit button. Please submit in the open browser window.");

            await button.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            await Task.Delay(4_000, cancellationToken);

            var body = await page.Locator("body").InnerTextAsync();
            var confirmed = SuccessText.IsMatch(body) || !string.Equals(page.Url, urlBefore, StringComparison.OrdinalIgnoreCase);

            if (confirmed)
            {
                await CloseSessionAsync(sessionId);
                return new ApplySubmitResult("Submitted", true, "Application submitted.");
            }

            return new ApplySubmitResult(
                "Unconfirmed", false,
                "Clicked Submit but no confirmation was detected. Check the browser window — there may be a missing required field, a CAPTCHA or an email verification step.");
        }
        catch (PlaywrightException ex)
        {
            return new ApplySubmitResult("Error", false, $"The browser window is no longer available ({FirstLine(ex.Message)}).");
        }
    }

    public Task CancelAsync(string sessionId) => CloseSessionAsync(sessionId);

    // ---------------------------------------------------------------
    // Form helpers
    // ---------------------------------------------------------------

    private static async Task OpenFormIfNeededAsync(IPage page)
    {
        if (await page.Locator("input[type=email], input[type=file]").CountAsync() > 0) return;

        var opener = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameRegex = ApplyOpenerName }).First;
        if (await opener.CountAsync() == 0)
            opener = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameRegex = ApplyOpenerName }).First;
        if (await opener.CountAsync() == 0) return;

        try
        {
            await opener.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await Task.Delay(1_500);
        }
        catch (PlaywrightException)
        {
            // Opener wasn't clickable — continue with whatever is on the page
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions FieldJson = new() { PropertyNameCaseInsensitive = true };

    private static async Task<List<FormField>> ScanFieldsAsync(IPage page)
    {
        var json = await page.EvaluateAsync<System.Text.Json.JsonElement>(ScanFieldsScript);
        return System.Text.Json.JsonSerializer.Deserialize<List<FormField>>(json, FieldJson) ?? [];
    }

    private static async Task FillTextFieldsAsync(IPage page, List<FormField> fields, CandidateProfile profile, List<string> filled)
    {
        var usedRules = new HashSet<string>();

        foreach (var f in fields)
        {
            if (f.Filled || f.Combo) continue;
            if (f.Type is "file" or "checkbox" or "radio" or "select" or "select-one" or "select-multiple") continue;
            if (f.Tag == "textarea") continue;

            var haystack = $"{f.Label} {f.Name} {f.Id}";
            if (NeverFill.IsMatch(haystack)) continue;

            foreach (var (label, value, display) in FieldRules)
            {
                if (!label.IsMatch(f.Label) && !label.IsMatch(f.Name) && !label.IsMatch(f.Id)) continue;

                var text = value(profile);
                if (!string.IsNullOrWhiteSpace(text) && usedRules.Add(display))
                {
                    await page.Locator($"[data-rm-idx='{f.Idx}']").FillAsync(text, new LocatorFillOptions { Timeout = 5_000 });
                    filled.Add(display);
                }
                break;
            }
        }
    }

    private static async Task<bool> UploadResumeAsync(IPage page, List<FormField> fields, byte[] pdf, string fileName)
    {
        var fileInputs = fields.Where(f => f.Type == "file").ToList();
        if (fileInputs.Count == 0) return false;

        var target = fileInputs.FirstOrDefault(f => Regex.IsMatch($"{f.Label} {f.Name} {f.Id}", "resume|cv", RegexOptions.IgnoreCase))
                     ?? fileInputs.FirstOrDefault(f => !Regex.IsMatch($"{f.Label} {f.Name} {f.Id}", "cover", RegexOptions.IgnoreCase));
        if (target is null) return false;

        await page.Locator($"[data-rm-idx='{target.Idx}']").SetInputFilesAsync(
            new FilePayload { Name = fileName, MimeType = "application/pdf", Buffer = pdf },
            new LocatorSetInputFilesOptions { Timeout = 10_000 });
        return true;
    }

    private static string Describe(FormField f)
    {
        var label = string.IsNullOrWhiteSpace(f.Label) ? (string.IsNullOrWhiteSpace(f.Name) ? f.Id : f.Name) : f.Label;
        label = Regex.Replace(label, @"\s+", " ").Trim().TrimEnd('*').Trim();
        return label.Length > 90 ? label[..90] + "…" : label;
    }

    // ---------------------------------------------------------------
    // Browser / session lifecycle
    // ---------------------------------------------------------------

    private async Task<IBrowser> GetBrowserAsync()
    {
        await _browserLock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true }) return _browser;

            _playwright ??= await Playwright.CreateAsync();
            _browser = await LaunchAnyAsync();
            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    // Prefer the user's own Chrome/Edge (no download needed); fall back to Playwright's bundled Chromium
    private async Task<IBrowser> LaunchAnyAsync()
    {
        foreach (var channel in _channels)
        {
            try
            {
                return await _playwright!.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions { Headless = _headless, Channel = channel });
            }
            catch (PlaywrightException)
            {
                // That browser isn't installed — try the next one
            }
        }

        try
        {
            return await LaunchBundledAsync();
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            // Last resort: download Chromium once (~150 MB)
            if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
                throw new InvalidOperationException(
                    "No Chrome or Edge browser was found and Chromium could not be downloaded. Install Google Chrome or Microsoft Edge and try again.");
            return await LaunchBundledAsync();
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                $"Could not open a browser window for auto-apply ({FirstLine(ex.Message)}). " +
                "Auto-apply needs the API running on your own computer (not in Docker).");
        }
    }

    private Task<IBrowser> LaunchBundledAsync()
        => _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _headless });

    private async Task CloseSessionAsync(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session)) return;
        try { await session.Context.CloseAsync(); }
        catch (PlaywrightException) { /* window already closed by the user */ }
    }

    private void PurgeExpiredSessions()
    {
        foreach (var (id, s) in _sessions)
            if (DateTime.UtcNow - s.Created > SessionLifetime)
                _ = CloseSessionAsync(id);
    }

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _sessions.Keys) await CloseSessionAsync(id);
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    private sealed record Session(IBrowserContext Context, IPage Page, DateTime Created);

    private sealed class FormField
    {
        public int Idx { get; init; }
        public string Tag { get; init; } = "";
        public string Type { get; init; } = "";
        public string Label { get; init; } = "";
        public string Name { get; init; } = "";
        public string Id { get; init; } = "";
        public bool Required { get; init; }
        public bool Filled { get; init; }
        public bool Combo { get; init; }
    }
}
