using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages;

[Authorize(Policy = "Grant:AdminAccess")]
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

    // OLD approach (embedding returnUrl in tokenConsumerURL - BROKEN)
    public string OldCallbackUrl { get; set; } = string.Empty;
    public string OldFinalGriffinUrl { get; set; } = string.Empty;

    // NEW approach (clean tokenConsumerURL, returnUrl stored in cookie - CORRECT)
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

    // ========== Live token-exchange comparison (POST /GriffinDiagnostic?handler=TestExchange) ==========
    // Pasted by the admin from a real Griffin redirect (the value of ?hashedToken= on the
    // browser callback URL). One-shot — Griffin invalidates the hash after first use, so the
    // admin will need a fresh redirect for each test cycle.
    [BindProperty]
    public string? TestHashedToken { get; set; }

    public bool RanLiveTest { get; set; }
    public TokenExchangeAttempt? AttemptCurlHash { get; set; }       // Raw GET ?hash= (mirrors working curl)
    public TokenExchangeAttempt? AttemptViaService { get; set; }     // Through GriffinService (production code path)
    public TokenExchangeAttempt? AttemptCurlToken { get; set; }      // Raw GET ?token= (control — proves the bug)
    public TokenExchangeAttempt? AttemptClaimsChain { get; set; }    // Optional follow-up: getClaims with the JWT
    public string? ExtractedJwtMasked { get; set; }

    /// <summary>
    /// One round-trip's worth of evidence: the URL we hit, what came back, how long it took.
    /// Rendered side-by-side with sibling attempts so a human can spot which boundary differs.
    /// </summary>
    public sealed class TokenExchangeAttempt
    {
        public string Mode { get; set; } = "";              // Human-readable label
        public string MaskedUrl { get; set; } = "";         // URL with hash/JWT masked
        public string CurlEquivalent { get; set; } = "";    // Copy-pastable curl command (also masked)
        public bool Success { get; set; }
        public int? HttpStatus { get; set; }
        public string? ErrorToken { get; set; }             // Service-mode only (e.g., GRIFFIN-TOKENEXCHANGE-200)
        public string? ResponseBodyPreview { get; set; }
        public int DurationMs { get; set; }
        public string? Exception { get; set; }
    }

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
            if (GriffinConfig.BaseUrl?.Contains("//", StringComparison.Ordinal) == true && !GriffinConfig.BaseUrl.StartsWith("http", StringComparison.Ordinal))
            {
                Warnings.Add("BaseUrl contains '//' but doesn't start with 'http://' or 'https://'. Did you mean to add the scheme?");
            }

            if (GriffinConfig.TokenConsumerUrl?.StartsWith("/", StringComparison.Ordinal) == true && !GriffinConfig.TokenConsumerUrl.StartsWith("http", StringComparison.Ordinal))
            {
                Warnings.Add("Callback URL starts with '/' (looks like a relative path). It should be a full URL like 'http://localhost:5000/Auth/GriffinCallback'");
            }

            if (GriffinConfig.BaseUrl?.EndsWith("/", StringComparison.Ordinal) == true)
            {
                Warnings.Add("BaseUrl ends with a trailing slash. This is usually fine, but may cause double-slash in generated URLs.");
            }

            // 8. Verify unauthenticated config resolution
            Checks.Add("8. Checking config availability for unauthenticated users (login page)...");
            try
            {
                var anyEnabledConfig = await _griffinConfigService.GetAnyEnabledGriffinConfigAsync();
                if (anyEnabledConfig != null)
                {
                    Checks.Add($"   ✓ GetAnyEnabledGriffinConfigAsync() found config (CompanyId={anyEnabledConfig.CompanyId})");
                    Checks.Add("   ✓ Unauthenticated users on login page WILL see the ADFS button");
                    ValidationResults["UnauthenticatedConfigAvailable"] = true;
                }
                else
                {
                    Checks.Add("   ✗ GetAnyEnabledGriffinConfigAsync() returned null");
                    Checks.Add("   ✗ Unauthenticated users on login page will NOT see the ADFS button");
                    Warnings.Add("No enabled Griffin config found in database. Unauthenticated users cannot use ADFS SSO from the login page.");
                    ValidationResults["UnauthenticatedConfigAvailable"] = false;
                }
            }
            catch (Exception ex)
            {
                Checks.Add($"   ⚠ Error checking unauthenticated config: {ex.Message}");
                ValidationResults["UnauthenticatedConfigAvailable"] = false;
            }

            // 9. Verify returnUrl cookie approach
            Checks.Add("9. Verifying returnUrl handling...");
            Checks.Add("   ✓ returnUrl is stored in 'griffin.returnUrl' cookie (HttpOnly, 5min TTL, /Auth path)");
            Checks.Add("   ✓ tokenConsumerURL sent to Griffin is clean (no embedded query params)");
            Checks.Add("   ✓ GriffinCallback reads returnUrl from cookie if not in query string");

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
                    ReturnUrlHandling = "cookie (griffin.returnUrl, HttpOnly, 5min TTL, /Auth path)",
                    TokenConsumerUrlClean = "no query params embedded — matches DOOF pattern",
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
                <p><strong>Current value:</strong> <code>" + HtmlEncoder.Default.Encode(GriffinConfig.BaseUrl ?? "") + @"</code></p>
                <p><strong>Expected value:</strong> <code>https://7108dev.d8200.mil</code> (or similar)</p>
                <p><strong>Why this causes the 404 error:</strong></p>
                <ul>
                    <li>When BuildAuthenticationUrl() constructs the final URL, it becomes: <code>" + HtmlEncoder.Default.Encode(GriffinConfig.BaseUrl ?? "") + @"/authentication?...</code></li>
                    <li>ASP.NET's <code>Redirect()</code> method sees this URL doesn't start with a scheme</li>
                    <li>It treats it as a RELATIVE path instead of an absolute URL</li>
                    <li>The browser tries to navigate to: <code>http://localhost:5000/" + HtmlEncoder.Default.Encode(GriffinConfig.BaseUrl ?? "") + @"/authentication?...</code></li>
                    <li>This path doesn't exist, resulting in a 404 error</li>
                </ul>";

            FixInstructions = @"
                <ol>
                    <li>Navigate to <a href='/Owner/GriffinConfig'>http://localhost:5000/Owner/GriffinConfig</a></li>
                    <li>Update the <strong>Base URL</strong> field to include the scheme</li>
                    <li>Change from: <code>" + HtmlEncoder.Default.Encode(GriffinConfig.BaseUrl ?? "") + @"</code></li>
                    <li>Change to: <code>https://" + HtmlEncoder.Default.Encode(GriffinConfig.BaseUrl ?? "") + @"</code></li>
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
                <p><strong>Current value:</strong> <code>" + HtmlEncoder.Default.Encode(GriffinConfig.TokenConsumerUrl ?? "") + @"</code></p>
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
                    <li>Change from: <code>" + HtmlEncoder.Default.Encode(GriffinConfig.TokenConsumerUrl ?? "") + @"</code></li>
                    <li>Change to: <code>http://localhost:5000" + HtmlEncoder.Default.Encode(GriffinConfig.TokenConsumerUrl ?? "") + @"</code></li>
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
                <p><strong>Generated URL:</strong> <code>" + HtmlEncoder.Default.Encode(GeneratedAuthUrl ?? "") + @"</code></p>
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
        // OLD APPROACH (Embedding returnUrl in tokenConsumerURL - BROKEN)
        // ===========================================
        // Appending ?returnUrl=... to the callback URL introduces a '?' that breaks
        // the outer Griffin auth URL's query string parsing.
        var oldCallback = GriffinConfig.TokenConsumerUrl + "?returnUrl=" + ReturnUrlEncoded3x;
        OldCallbackUrl = oldCallback;
        OldFinalGriffinUrl = $"{GriffinConfig.BaseUrl?.TrimEnd('/')}/authentication?tokenConsumerURL={oldCallback}";
        OldUrlLooksLikeUrl = OldFinalGriffinUrl.Contains("tokenConsumerURL=https://", StringComparison.Ordinal) ||
                             OldFinalGriffinUrl.Contains("tokenConsumerURL=http://", StringComparison.Ordinal);

        // ===========================================
        // NEW APPROACH (Clean tokenConsumerURL, returnUrl in cookie - CORRECT)
        // ===========================================
        // The tokenConsumerURL is sent as-is (no query params embedded).
        // returnUrl is stored in a temporary cookie and read in the callback.
        // This matches how DOOF keeps its tokenConsumerURL clean.
        NewCallbackUrl = GriffinConfig.TokenConsumerUrl;  // Clean URL, no query params
        NewFinalGriffinUrl = $"{GriffinConfig.BaseUrl?.TrimEnd('/')}/authentication?tokenConsumerURL={GriffinConfig.TokenConsumerUrl}";
        NewUrlLooksLikeUrl = NewFinalGriffinUrl.Contains("tokenConsumerURL=https://", StringComparison.Ordinal) ||
                             NewFinalGriffinUrl.Contains("tokenConsumerURL=http://", StringComparison.Ordinal);

        // ===========================================
        // DOOF APPROACH (Reference - WORKS)
        // ===========================================
        // Doof encodes only the destination 3x, appends to base callback URL
        DoofDestinationEncoded3x = ReturnUrlEncoded3x;  // Same as ours
        DoofCallbackUrl = "https://doof.d8200.mil/api/login/" + DoofDestinationEncoded3x;
        DoofFinalGriffinUrl = $"https://doof-auth-adfs.d8200.mil/authentication?tokenConsumerURL={DoofCallbackUrl}";
        DoofUrlLooksLikeUrl = DoofFinalGriffinUrl.Contains("tokenConsumerURL=https://", StringComparison.Ordinal);

        Checks.Add("📊 URL Comparison Generated:");
        var oldUrlStart = OldFinalGriffinUrl.IndexOf("tokenConsumerURL=", StringComparison.Ordinal);
        if (oldUrlStart >= 0)
        {
            oldUrlStart += 17; // Length of "tokenConsumerURL="
            var previewLength = Math.Min(20, OldFinalGriffinUrl.Length - oldUrlStart);
            Checks.Add($"   OLD (broken): tokenConsumerURL had embedded ?returnUrl=... → '{OldFinalGriffinUrl.Substring(oldUrlStart, previewLength)}...'");
        }
        Checks.Add($"   CURRENT (fixed): tokenConsumerURL is clean — returnUrl stored in cookie");
        Checks.Add($"   DOOF (reference): tokenConsumerURL is clean — destination in path");
    }

    // ============================================================================================
    // Live token-exchange comparison
    //
    // The user's recurring "even after multiple refactors, ADFS still 400s" pattern was caused by
    // a query parameter mismatch: claimToken takes ?hash=, but our service was sending ?token=.
    // To make any future regression like this immediately visible — and to give an admin in the
    // air-gapped env a way to compare service-vs-curl behavior without leaving the app — this
    // handler runs three independent attempts on a single user-supplied hashed token:
    //
    //   (A) Raw HTTP GET with ?hash=     (mirrors the working curl)
    //   (B) Through GriffinService       (the production code path)
    //   (C) Raw HTTP GET with ?token=    (control — should fail; proves ?hash= is the contract)
    //
    // If (B) succeeds we also chain a getClaims call so the full happy-path is visible too.
    //
    // SECURITY-AUDITED: Page is gated by Grant:AdminAccess. The hashed token is supplied by
    // the authenticated admin and used only against the configured BaseUrl. Token values are
    // masked (first 4 + last 4) in rendered output to limit shoulder-surfing / screenshot leak.
    // ============================================================================================
    public async Task<IActionResult> OnPostTestExchangeAsync()
    {
        // Re-run the read-only diagnostic so the rest of the page renders with up-to-date state.
        await OnGetAsync();

        if (string.IsNullOrWhiteSpace(TestHashedToken))
        {
            Warnings.Add("Live token-exchange test was triggered without a hashed token. Paste the value of '?hashedToken=' from the Griffin redirect URL and resubmit.");
            return Page();
        }

        if (GriffinConfig?.BaseUrl is null || BaseUrlHasScheme != true)
        {
            Warnings.Add("Live token-exchange test cannot run: BaseUrl is missing or invalid. Fix the configuration first.");
            return Page();
        }

        RanLiveTest = true;
        var hash = TestHashedToken.Trim();
        var baseUrl = GriffinConfig.BaseUrl.TrimEnd('/');
        var timeoutSeconds = GriffinConfig.TimeoutSeconds > 0 ? GriffinConfig.TimeoutSeconds : 10;

        // Three independent calls — fan out concurrently so the page returns quickly even if one hangs.
        var rawHashTask = RunRawAttemptAsync(
            baseUrl, hash, paramName: "hash", timeoutSeconds,
            label: "(A) Raw HTTP GET ?hash=  — mirrors the working curl");
        var serviceTask = RunServiceAttemptAsync(hash, baseUrl, timeoutSeconds);
        var rawTokenTask = RunRawAttemptAsync(
            baseUrl, hash, paramName: "token", timeoutSeconds,
            label: "(C) Raw HTTP GET ?token=  — control: should fail with HTTP 400");

        await Task.WhenAll(rawHashTask, serviceTask, rawTokenTask);

        AttemptCurlHash = rawHashTask.Result;
        AttemptViaService = serviceTask.Result;
        AttemptCurlToken = rawTokenTask.Result;

        // Chain step: if the service exchange returned a JWT, follow up with getClaims
        // so the diagnostic shows the full happy-path the user reproduced manually with two curls.
        if (AttemptViaService.Success && !string.IsNullOrWhiteSpace(AttemptViaService.ResponseBodyPreview))
        {
            // The service-mode preview already holds the parsed JWT (ExchangeTokenAsync extracts it).
            var jwt = AttemptViaService.ResponseBodyPreview!.Trim();
            // Strip a trailing ellipsis our truncator may have added.
            if (jwt.EndsWith("…", StringComparison.Ordinal)) jwt = jwt[..^1];
            ExtractedJwtMasked = MaskSecret(jwt);
            AttemptClaimsChain = await RunClaimsChainAsync(baseUrl, jwt, timeoutSeconds);
        }

        return Page();
    }

    private async Task<TokenExchangeAttempt> RunRawAttemptAsync(
        string baseUrl, string hash, string paramName, int timeoutSeconds, string label)
    {
        var url = $"{baseUrl}/authentication/claimToken?{paramName}={Uri.EscapeDataString(hash)}";
        var attempt = new TokenExchangeAttempt
        {
            Mode = label,
            MaskedUrl = MaskUrlSecret(url),
            CurlEquivalent = $"curl -X GET '{MaskUrlSecret(url)}' -H 'accept: */*'",
        };

        var sw = Stopwatch.StartNew();
        try
        {
            // Bare HttpClient (no factory). Deliberately chosen so this attempt isolates
            // *transport* variables — if it succeeds while the service path fails, the
            // delta is somewhere in our HttpClientFactory pipeline, not the URL contract.
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Clear();
            req.Headers.Accept.ParseAdd("*/*");
            using var response = await client.SendAsync(req);
            attempt.HttpStatus = (int)response.StatusCode;
            attempt.Success = response.IsSuccessStatusCode;
            var body = await response.Content.ReadAsStringAsync();
            attempt.ResponseBodyPreview = TruncateForDisplay(body);
        }
        catch (Exception ex)
        {
            attempt.Exception = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            sw.Stop();
            attempt.DurationMs = (int)sw.ElapsedMilliseconds;
        }

        return attempt;
    }

    private async Task<TokenExchangeAttempt> RunServiceAttemptAsync(string hash, string baseUrl, int timeoutSeconds)
    {
        var attempt = new TokenExchangeAttempt
        {
            Mode = "(B) Through GriffinService.ExchangeTokenAsync — production code path",
            MaskedUrl = $"{baseUrl}/authentication/claimToken?hash={MaskSecret(hash)}",
            CurlEquivalent = "(uses HttpClientFactory \"GriffinClient\" — same pipeline as the live login flow)",
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _griffinService.ExchangeTokenAsync(hash, baseUrl, timeoutSeconds);
            attempt.Success = result.Success;
            if (result.Success)
            {
                attempt.HttpStatus = 200;
                attempt.ResponseBodyPreview = TruncateForDisplay(result.Value ?? string.Empty);
            }
            else if (result.Error is not null)
            {
                attempt.HttpStatus = result.Error.HttpStatus;
                attempt.ErrorToken = result.Error.ErrorToken;
                attempt.ResponseBodyPreview = TruncateForDisplay(result.Error.ResponsePreview ?? result.Error.TechnicalDetail ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            attempt.Exception = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            sw.Stop();
            attempt.DurationMs = (int)sw.ElapsedMilliseconds;
        }

        return attempt;
    }

    private async Task<TokenExchangeAttempt> RunClaimsChainAsync(string baseUrl, string jwt, int timeoutSeconds)
    {
        var url = $"{baseUrl}/authorization/getClaims?token={Uri.EscapeDataString(jwt)}";
        var attempt = new TokenExchangeAttempt
        {
            Mode = "Chain follow-up: GET /authorization/getClaims?token=<JWT>",
            MaskedUrl = MaskUrlSecret(url),
            CurlEquivalent = $"curl -X GET '{MaskUrlSecret(url)}' -H 'accept: */*'",
        };

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Clear();
            req.Headers.Accept.ParseAdd("*/*");
            using var response = await client.SendAsync(req);
            attempt.HttpStatus = (int)response.StatusCode;
            attempt.Success = response.IsSuccessStatusCode;
            var body = await response.Content.ReadAsStringAsync();
            attempt.ResponseBodyPreview = TruncateForDisplay(body);
        }
        catch (Exception ex)
        {
            attempt.Exception = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            sw.Stop();
            attempt.DurationMs = (int)sw.ElapsedMilliseconds;
        }

        return attempt;
    }

    private static string TruncateForDisplay(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        const int max = 600;
        return body.Length > max ? body[..max] + "…" : body;
    }

    private static string MaskSecret(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= 8) return "***";
        return $"{s[..4]}…{s[^4..]}";
    }

    /// <summary>
    /// Replaces the value of the first hash= or token= query parameter with a masked version
    /// suitable for display. Preserves the surrounding URL structure so a developer can still
    /// see exactly which endpoint and parameter name was used.
    /// </summary>
    private static string MaskUrlSecret(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        foreach (var key in new[] { "hash=", "token=" })
        {
            var idx = url.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) continue;
            var valStart = idx + key.Length;
            var ampersand = url.IndexOf('&', valStart);
            var rawEncoded = ampersand < 0 ? url[valStart..] : url[valStart..ampersand];
            string decoded;
            try { decoded = Uri.UnescapeDataString(rawEncoded); }
            catch { decoded = rawEncoded; }
            var masked = MaskSecret(decoded);
            return url[..valStart] + masked + (ampersand < 0 ? string.Empty : url[ampersand..]);
        }
        return url;
    }
}
