using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Pages;

[Authorize(Policy = "IsAdmin")]
public class GriffinDiagnosticModel : PageModel
{
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IGriffinService _griffinService;
    private readonly ILogger<GriffinDiagnosticModel> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _dbContext;

    public GriffinConfig? GriffinConfig { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
    public string? GeneratedAuthUrl { get; set; }
    public bool? BaseUrlHasScheme { get; set; }
    public bool? CallbackUrlHasScheme { get; set; }
    public bool? AuthUrlIsAbsolute { get; set; }
    public string? UrlScheme { get; set; }
    public string? UrlHost { get; set; }
    public string? UrlPath { get; set; }
    public string? UrlQuery { get; set; }
    public string? RootCause { get; set; }
    public string? RootCauseExplanation { get; set; }
    public string? FixInstructions { get; set; }
    public string? DiagnosticJson { get; set; }

    // Enhanced diagnostics
    public bool CallbackPageExists { get; set; }
    public bool GriffinServerReachable { get; set; }
    public string? GriffinServerError { get; set; }
    public int? GriffinResponseTime { get; set; }
    public string? ConfigSource { get; set; }
    public bool HasAppSettingsFallback { get; set; }
    public string? CallbackUrlHost { get; set; }
    public string? CallbackUrlPath { get; set; }
    public bool CallbackUrlUsesLocalhost { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Checks { get; set; } = new();
    public Dictionary<string, bool> ValidationResults { get; set; } = new();

    // URL encoding comparison properties
    public string TestReturnUrl { get; set; } = "/Home/Index";
    public string ReturnUrlEncoded1x { get; set; } = string.Empty;
    public string ReturnUrlEncoded2x { get; set; } = string.Empty;
    public string ReturnUrlEncoded3x { get; set; } = string.Empty;

    // OLD approach (current - WRONG)
    public string OldCallbackUrl { get; set; } = string.Empty;
    public string OldFinalGriffinUrl { get; set; } = string.Empty;

    // NEW approach (proposed fix - CORRECT)
    public string NewCallbackUrl { get; set; } = string.Empty;
    public string NewFinalGriffinUrl { get; set; } = string.Empty;

    // DOOF approach (reference - WORKS)
    public string DoofDestinationEncoded3x { get; set; } = string.Empty;
    public string DoofCallbackUrl { get; set; } = string.Empty;
    public string DoofFinalGriffinUrl { get; set; } = string.Empty;

    // Analysis flags
    public bool OldUrlLooksLikeUrl { get; set; }
    public bool NewUrlLooksLikeUrl { get; set; }
    public bool DoofUrlLooksLikeUrl { get; set; }

    public GriffinDiagnosticModel(
        IGriffinConfigService griffinConfigService,
        IGriffinService griffinService,
        ILogger<GriffinDiagnosticModel> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        AppDbContext dbContext)
    {
        _griffinConfigService = griffinConfigService;
        _griffinService = griffinService;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _dbContext = dbContext;
    }

    public async Task OnGetAsync()
    {
        try
        {
            Checks.Add("Starting comprehensive Griffin ADFS diagnostic...");

            // 1. Check configuration source
            Checks.Add("1. Checking configuration source...");
            var dbConfig = await _dbContext.GriffinConfigs.FirstOrDefaultAsync();
            var appSettingsEnabled = _configuration.GetValue<bool>("Griffin:Enabled", false);
            var appSettingsBaseUrl = _configuration["Griffin:BaseUrl"];

            if (dbConfig != null)
            {
                ConfigSource = "Database";
                Checks.Add("   ✓ Configuration loaded from database");
                ValidationResults["ConfigInDatabase"] = true;
            }
            else if (appSettingsEnabled && !string.IsNullOrWhiteSpace(appSettingsBaseUrl))
            {
                ConfigSource = "appsettings.json (fallback)";
                Checks.Add("   ⚠ No database config - using appsettings.json fallback");
                Warnings.Add("Configuration is loaded from appsettings.json. This is a fallback and may not reflect saved settings.");
                ValidationResults["ConfigInDatabase"] = false;
            }
            else
            {
                ConfigSource = "None";
                Checks.Add("   ✗ No configuration found in database or appsettings.json");
                ValidationResults["ConfigInDatabase"] = false;
            }

            HasAppSettingsFallback = appSettingsEnabled && !string.IsNullOrWhiteSpace(appSettingsBaseUrl);

            // Load Griffin configuration
            GriffinConfig = await _griffinConfigService.GetGriffinConfigAsync();

            if (GriffinConfig == null)
            {
                _logger.LogWarning("No Griffin configuration found");
                Checks.Add("✗ DIAGNOSTIC STOPPED: No Griffin configuration available");
                return;
            }

            // 2. Validate BaseUrl
            Checks.Add("2. Validating Base URL...");
            if (!string.IsNullOrEmpty(GriffinConfig.BaseUrl))
            {
                BaseUrlHasScheme = Uri.TryCreate(GriffinConfig.BaseUrl, UriKind.Absolute, out var baseUri) &&
                                  (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps);

                if (BaseUrlHasScheme == true)
                {
                    Checks.Add($"   ✓ BaseUrl has valid scheme: {baseUri!.Scheme}://");
                    ValidationResults["BaseUrlScheme"] = true;
                }
                else
                {
                    Checks.Add($"   ✗ BaseUrl is MISSING scheme: '{GriffinConfig.BaseUrl}'");
                    ValidationResults["BaseUrlScheme"] = false;
                }
            }
            else
            {
                Checks.Add("   ✗ BaseUrl is empty or null");
                ValidationResults["BaseUrlScheme"] = false;
            }

            // 3. Validate TokenConsumerUrl (Callback URL)
            Checks.Add("3. Validating Callback URL...");
            if (!string.IsNullOrEmpty(GriffinConfig.TokenConsumerUrl))
            {
                CallbackUrlHasScheme = Uri.TryCreate(GriffinConfig.TokenConsumerUrl, UriKind.Absolute, out var callbackUri) &&
                                      (callbackUri.Scheme == Uri.UriSchemeHttp || callbackUri.Scheme == Uri.UriSchemeHttps);

                if (CallbackUrlHasScheme == true)
                {
                    Checks.Add($"   ✓ Callback URL has valid scheme: {callbackUri!.Scheme}://");
                    CallbackUrlHost = callbackUri.Host;
                    CallbackUrlPath = callbackUri.AbsolutePath;
                    CallbackUrlUsesLocalhost = callbackUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                              callbackUri.Host == "127.0.0.1";

                    if (CallbackUrlUsesLocalhost)
                    {
                        Warnings.Add($"Callback URL uses localhost ({callbackUri.Host}). This only works if Griffin server can reach localhost on your machine.");
                    }

                    // Check if path is correct
                    if (CallbackUrlPath == "/Auth/GriffinCallback")
                    {
                        Checks.Add($"   ✓ Callback path is correct: {CallbackUrlPath}");
                        ValidationResults["CallbackPath"] = true;
                    }
                    else
                    {
                        Checks.Add($"   ⚠ Callback path may be incorrect: {CallbackUrlPath} (expected: /Auth/GriffinCallback)");
                        Warnings.Add($"Callback path is '{CallbackUrlPath}' but should be '/Auth/GriffinCallback'");
                        ValidationResults["CallbackPath"] = false;
                    }

                    ValidationResults["CallbackUrlScheme"] = true;
                }
                else
                {
                    Checks.Add($"   ✗ Callback URL is MISSING scheme: '{GriffinConfig.TokenConsumerUrl}'");
                    ValidationResults["CallbackUrlScheme"] = false;
                }
            }
            else
            {
                Checks.Add("   ✗ Callback URL is empty or null");
                ValidationResults["CallbackUrlScheme"] = false;
            }

            // 4. Check if callback page exists
            Checks.Add("4. Checking if Griffin callback page exists...");
            try
            {
                var callbackPagePath = Path.Combine(Directory.GetCurrentDirectory(), "Pages", "Auth", "GriffinCallback.cshtml");
                CallbackPageExists = System.IO.File.Exists(callbackPagePath);

                if (CallbackPageExists)
                {
                    Checks.Add($"   ✓ Callback page found: {callbackPagePath}");
                    ValidationResults["CallbackPageExists"] = true;
                }
                else
                {
                    Checks.Add($"   ✗ Callback page NOT FOUND: {callbackPagePath}");
                    ValidationResults["CallbackPageExists"] = false;
                }
            }
            catch (Exception ex)
            {
                Checks.Add($"   ⚠ Could not check callback page: {ex.Message}");
                ValidationResults["CallbackPageExists"] = false;
            }

            // 5. Generate and validate authentication URL
            Checks.Add("5. Generating authentication URL...");
            if (!string.IsNullOrWhiteSpace(GriffinConfig.BaseUrl) &&
                !string.IsNullOrWhiteSpace(GriffinConfig.TokenConsumerUrl))
            {
                try
                {
                    GeneratedAuthUrl = _griffinService.BuildAuthenticationUrl(
                        GriffinConfig.BaseUrl,
                        GriffinConfig.TokenConsumerUrl);

                    Checks.Add($"   Generated URL: {GeneratedAuthUrl.Substring(0, Math.Min(100, GeneratedAuthUrl.Length))}...");

                    // Parse the generated URL
                    if (Uri.TryCreate(GeneratedAuthUrl, UriKind.Absolute, out var authUri))
                    {
                        AuthUrlIsAbsolute = true;
                        UrlScheme = authUri.Scheme;
                        UrlHost = authUri.Host;
                        UrlPath = authUri.AbsolutePath;
                        UrlQuery = authUri.Query;

                        Checks.Add("   ✓ Generated URL is ABSOLUTE (this is correct!)");
                        Checks.Add($"   ✓ Scheme: {UrlScheme}");
                        Checks.Add($"   ✓ Host: {UrlHost}");
                        Checks.Add($"   ✓ Path: {UrlPath}");
                        ValidationResults["AuthUrlAbsolute"] = true;

                        // Validate path is correct
                        if (UrlPath == "/authentication")
                        {
                            Checks.Add("   ✓ Path is correct: /authentication");
                            ValidationResults["AuthUrlPath"] = true;
                        }
                        else
                        {
                            Checks.Add($"   ⚠ Path may be incorrect: {UrlPath} (expected: /authentication)");
                            ValidationResults["AuthUrlPath"] = false;
                        }
                    }
                    else
                    {
                        AuthUrlIsAbsolute = false;
                        Checks.Add($"   ✗ Generated URL is RELATIVE (this causes 404 errors!)");
                        Checks.Add($"   ✗ URL: {GeneratedAuthUrl}");
                        ValidationResults["AuthUrlAbsolute"] = false;
                        _logger.LogWarning("Generated auth URL is not absolute: {Url}", GeneratedAuthUrl);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate authentication URL");
                    ErrorMessage = $"Failed to generate authentication URL: {ex.Message}";
                    Checks.Add($"   ✗ Error generating URL: {ex.Message}");
                    ValidationResults["AuthUrlAbsolute"] = false;
                    HasError = true;
                }
            }
            else
            {
                Checks.Add("   ✗ Cannot generate URL - BaseUrl or TokenConsumerUrl is missing");
                ValidationResults["AuthUrlAbsolute"] = false;
            }

            // 6. Test Griffin server connectivity
            Checks.Add("6. Testing Griffin server connectivity...");
            if (BaseUrlHasScheme == true && !string.IsNullOrWhiteSpace(GriffinConfig.BaseUrl))
            {
                try
                {
                    var result = await _griffinConfigService.TestConnectionAsync(
                        GriffinConfig.BaseUrl,
                        GriffinConfig.TimeoutSeconds);

                    GriffinServerReachable = result.Success;
                    GriffinResponseTime = result.DurationMs;

                    if (result.Success)
                    {
                        Checks.Add($"   ✓ Griffin server is REACHABLE ({result.DurationMs}ms, HTTP {result.StatusCode})");
                        ValidationResults["GriffinReachable"] = true;
                    }
                    else
                    {
                        Checks.Add($"   ✗ Griffin server is NOT reachable: {result.ErrorMessage}");
                        GriffinServerError = result.ErrorMessage;
                        ValidationResults["GriffinReachable"] = false;
                    }
                }
                catch (Exception ex)
                {
                    Checks.Add($"   ✗ Error testing connectivity: {ex.Message}");
                    GriffinServerError = ex.Message;
                    ValidationResults["GriffinReachable"] = false;
                }
            }
            else
            {
                Checks.Add("   ⚠ Skipping connectivity test - BaseUrl is invalid");
                ValidationResults["GriffinReachable"] = false;
            }

            // 7. Additional checks
            Checks.Add("7. Running additional validation checks...");

            // Check for common mistakes
            if (GriffinConfig.BaseUrl?.Contains("//") == true && !GriffinConfig.BaseUrl.StartsWith("http"))
            {
                Warnings.Add("BaseUrl contains '//' but doesn't start with 'http://' or 'https://'. Did you mean to add the scheme?");
            }

            if (GriffinConfig.TokenConsumerUrl?.StartsWith("/") == true && !GriffinConfig.TokenConsumerUrl.StartsWith("http"))
            {
                Warnings.Add("Callback URL starts with '/' (looks like a relative path). It should be a full URL like 'http://localhost:5000/Auth/GriffinCallback'");
            }

            if (GriffinConfig.BaseUrl?.EndsWith("/") == true)
            {
                Warnings.Add("BaseUrl ends with a trailing slash. This is usually fine, but may cause double-slash in generated URLs.");
            }

            Checks.Add("✓ Diagnostic complete!");

            // Determine root cause
            AnalyzeRootCause();

            // Generate URL encoding comparison
            GenerateUrlComparison();

            // Generate diagnostic JSON
            var diagnosticData = new
            {
                Timestamp = DateTime.UtcNow,
                GriffinConfig = new
                {
                    GriffinConfig.Id,
                    GriffinConfig.CompanyId,
                    GriffinConfig.Enabled,
                    GriffinConfig.BaseUrl,
                    GriffinConfig.TokenConsumerUrl,
                    GriffinConfig.AutoProvisionUsers,
                    GriffinConfig.DefaultProvisionedRole,
                    GriffinConfig.TimeoutSeconds
                },
                Analysis = new
                {
                    BaseUrlHasScheme,
                    CallbackUrlHasScheme,
                    GeneratedAuthUrl,
                    AuthUrlIsAbsolute,
                    UrlComponents = new
                    {
                        UrlScheme,
                        UrlHost,
                        UrlPath,
                        UrlQuery
                    },
                    RootCause,
                    RootCauseExplanation,
                    FixInstructions
                }
            };

            DiagnosticJson = JsonSerializer.Serialize(diagnosticData, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostic page failed");
            ErrorMessage = $"Diagnostic failed: {ex.Message}\n\n{ex.StackTrace}";
            HasError = true;
        }
    }

    private void AnalyzeRootCause()
    {
        if (GriffinConfig == null)
        {
            return;
        }

        // Check for missing scheme in BaseUrl
        if (BaseUrlHasScheme == false)
        {
            RootCause = "🔴 CRITICAL: BaseUrl is missing the http:// or https:// scheme";
            RootCauseExplanation = @"
                <p>The <code>BaseUrl</code> field in the database does not start with <code>http://</code> or <code>https://</code>.</p>
                <p><strong>Current value:</strong> <code>" + GriffinConfig.BaseUrl + @"</code></p>
                <p><strong>Expected value:</strong> <code>https://7108dev.d8200.mil</code> (or similar)</p>
                <p><strong>Why this causes the 404 error:</strong></p>
                <ul>
                    <li>When BuildAuthenticationUrl() constructs the final URL, it becomes: <code>" + GriffinConfig.BaseUrl + @"/authentication?...</code></li>
                    <li>ASP.NET's <code>Redirect()</code> method sees this URL doesn't start with a scheme</li>
                    <li>It treats it as a RELATIVE path instead of an absolute URL</li>
                    <li>The browser tries to navigate to: <code>http://localhost:5000/" + GriffinConfig.BaseUrl + @"/authentication?...</code></li>
                    <li>This path doesn't exist, resulting in a 404 error</li>
                </ul>";

            FixInstructions = @"
                <ol>
                    <li>Navigate to <a href='/Owner/GriffinConfig'>http://localhost:5000/Owner/GriffinConfig</a></li>
                    <li>Update the <strong>Base URL</strong> field to include the scheme</li>
                    <li>Change from: <code>" + GriffinConfig.BaseUrl + @"</code></li>
                    <li>Change to: <code>https://" + GriffinConfig.BaseUrl + @"</code></li>
                    <li>Click <strong>Save</strong></li>
                    <li>Click <strong>Test Connection</strong> to verify</li>
                    <li>Try <strong>Login with ADFS</strong> again</li>
                </ol>";
            return;
        }

        // Check for missing scheme in TokenConsumerUrl
        if (CallbackUrlHasScheme == false)
        {
            RootCause = "🔴 CRITICAL: TokenConsumerUrl is missing the http:// or https:// scheme";
            RootCauseExplanation = @"
                <p>The <code>TokenConsumerUrl</code> (callback URL) field in the database does not start with <code>http://</code> or <code>https://</code>.</p>
                <p><strong>Current value:</strong> <code>" + GriffinConfig.TokenConsumerUrl + @"</code></p>
                <p><strong>Expected value:</strong> <code>http://localhost:5000/Auth/GriffinCallback</code></p>
                <p><strong>Why this might cause issues:</strong></p>
                <ul>
                    <li>The callback URL is double-encoded and sent to Griffin ADFS</li>
                    <li>Griffin ADFS might not correctly parse a relative callback URL</li>
                    <li>After authentication, Griffin won't know where to redirect the user back to</li>
                </ul>";

            FixInstructions = @"
                <ol>
                    <li>Navigate to <a href='/Owner/GriffinConfig'>http://localhost:5000/Owner/GriffinConfig</a></li>
                    <li>Update the <strong>Callback URL</strong> field to include the scheme</li>
                    <li>Change from: <code>" + GriffinConfig.TokenConsumerUrl + @"</code></li>
                    <li>Change to: <code>http://localhost:5000" + GriffinConfig.TokenConsumerUrl + @"</code></li>
                    <li>Click <strong>Save</strong></li>
                    <li>Try <strong>Login with ADFS</strong> again</li>
                </ol>";
            return;
        }

        // Check if generated URL is not absolute
        if (AuthUrlIsAbsolute == false && !string.IsNullOrEmpty(GeneratedAuthUrl))
        {
            RootCause = "🔴 CRITICAL: Generated authentication URL is not absolute";
            RootCauseExplanation = @"
                <p>The URL generated by <code>BuildAuthenticationUrl()</code> is not recognized as an absolute URL.</p>
                <p><strong>Generated URL:</strong> <code>" + GeneratedAuthUrl + @"</code></p>
                <p>This indicates a problem with how the URL is being constructed from the BaseUrl and TokenConsumerUrl.</p>";

            FixInstructions = @"
                <p>Please verify both BaseUrl and TokenConsumerUrl have valid schemes (http:// or https://).</p>
                <p>If both fields look correct, there may be a code bug. Please report this issue.</p>";
            return;
        }

        // No obvious issues
        if (BaseUrlHasScheme == true && CallbackUrlHasScheme == true && AuthUrlIsAbsolute == true)
        {
            RootCause = null;
            RootCauseExplanation = null;
            FixInstructions = @"
                <p class='status-ok'>✓ Configuration appears correct!</p>
                <p>If you're still experiencing issues:</p>
                <ol>
                    <li>Check the Griffin ADFS server logs (if accessible)</li>
                    <li>Verify the callback URL is whitelisted on the Griffin server</li>
                    <li>Try using the server's IP address instead of <code>localhost</code></li>
                    <li>Check for any firewalls or network restrictions</li>
                </ol>";
        }
    }

    private void GenerateUrlComparison()
    {
        if (GriffinConfig == null || string.IsNullOrWhiteSpace(GriffinConfig.TokenConsumerUrl))
            return;

        // Test with sample return URL
        TestReturnUrl = "/Home/Index";

        // Show encoding progression
        ReturnUrlEncoded1x = Uri.EscapeDataString(TestReturnUrl);
        ReturnUrlEncoded2x = Uri.EscapeDataString(ReturnUrlEncoded1x);
        ReturnUrlEncoded3x = Uri.EscapeDataString(ReturnUrlEncoded2x);

        // ===========================================
        // OLD APPROACH (Current - WRONG)
        // ===========================================
        var oldCallback = GriffinConfig.TokenConsumerUrl + "?returnUrl=" + ReturnUrlEncoded1x;

        // Simulate current GriffinService.BuildAuthenticationUrl() - double-encodes ENTIRE URL
        var oldEncoded1x = Uri.EscapeDataString(oldCallback);
        var oldEncoded2x = Uri.EscapeDataString(oldEncoded1x);

        OldCallbackUrl = oldEncoded2x;  // This is gibberish!
        OldFinalGriffinUrl = $"{GriffinConfig.BaseUrl?.TrimEnd('/')}/authentication?tokenConsumerURL={oldEncoded2x}";
        OldUrlLooksLikeUrl = OldFinalGriffinUrl.Contains("tokenConsumerURL=https://") ||
                             OldFinalGriffinUrl.Contains("tokenConsumerURL=http://");

        // ===========================================
        // NEW APPROACH (Proposed Fix - CORRECT)
        // ===========================================
        var newCallback = GriffinConfig.TokenConsumerUrl + "?returnUrl=" + ReturnUrlEncoded3x;

        // New GriffinService.BuildAuthenticationUrl() - does NOT encode the entire URL
        NewCallbackUrl = newCallback;  // Readable URL structure!
        NewFinalGriffinUrl = $"{GriffinConfig.BaseUrl?.TrimEnd('/')}/authentication?tokenConsumerURL={newCallback}";
        NewUrlLooksLikeUrl = NewFinalGriffinUrl.Contains("tokenConsumerURL=https://") ||
                             NewFinalGriffinUrl.Contains("tokenConsumerURL=http://");

        // ===========================================
        // DOOF APPROACH (Reference - WORKS)
        // ===========================================
        // Doof encodes only the destination 3x, appends to base callback URL
        DoofDestinationEncoded3x = ReturnUrlEncoded3x;  // Same as ours
        DoofCallbackUrl = "https://doof.d8200.mil/api/login/" + DoofDestinationEncoded3x;
        DoofFinalGriffinUrl = $"https://doof-auth-adfs.d8200.mil/authentication?tokenConsumerURL={DoofCallbackUrl}";
        DoofUrlLooksLikeUrl = DoofFinalGriffinUrl.Contains("tokenConsumerURL=https://");

        Checks.Add("📊 URL Comparison Generated:");
        var oldUrlStart = OldFinalGriffinUrl.IndexOf("tokenConsumerURL=");
        if (oldUrlStart >= 0)
        {
            oldUrlStart += 17; // Length of "tokenConsumerURL="
            var previewLength = Math.Min(20, OldFinalGriffinUrl.Length - oldUrlStart);
            Checks.Add($"   OLD (current): tokenConsumerURL starts with '{OldFinalGriffinUrl.Substring(oldUrlStart, previewLength)}...'");
        }
        Checks.Add($"   NEW (proposed): tokenConsumerURL starts with 'http://' or 'https://'");
        Checks.Add($"   DOOF (working): tokenConsumerURL starts with 'https://'");
    }
}
