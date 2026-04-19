using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.OpenApi.Models;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Models.Support;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Reflection;
using ShiftManager.Middleware;
using ShiftManager.Authorization;
using ShiftManager.Hubs;
using ShiftManager.Data.SeedData;

// F-01: Top-level try-catch for IIS deployment diagnostics.
// When the app crashes during startup under IIS (HTTP 500.30), stdout may not be captured.
// This writes the fatal exception to a file next to the exe for offline troubleshooting.
try
{

// Deployment Export: Phase 1 — restore config + DataProtection keys before builder reads them
// This must run before WebApplication.CreateBuilder() which freezes IConfiguration and loads DP keys
DeploymentExportService.RestoreConfigAndKeys();

var builder = WebApplication.CreateBuilder(args);

// B-022: Logging Configuration
// - Clean console output in development for better readability
// - JSON structured format in production for log aggregation
builder.Logging.ClearProviders();

if (builder.Environment.IsDevelopment())
{
    // Development: Simple, readable console output
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = false;
        options.TimestampFormat = "HH:mm:ss ";
        options.IncludeScopes = false;
    });
    
    // Reduce noise from EF Core and ASP.NET in development
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Information);
    builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.Information);
}
else
{
    // Production: JSON structured logging for log aggregation systems
    builder.Logging.AddJsonConsole(options =>
    {
        options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
        {
            Indented = false // Compact JSON for log aggregation
        };
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; // ISO 8601 format
        options.UseUtcTimestamp = true;
    });

    // F-01/F-02: Write critical events to Windows Event Log for IIS monitoring
    if (OperatingSystem.IsWindows())
    {
#pragma warning disable CA1416 // Platform compatibility — guarded by IsWindows()
        try
        {
            builder.Logging.AddEventLog(settings =>
            {
                settings.SourceName = "ShiftManager";
                settings.LogName = "Application";
                settings.Filter = (category, level) => level >= LogLevel.Warning;
            });
        }
        catch (Exception)
        {
            // EventLog source creation requires admin privileges which IIS app pool identity
            // may not have. Swallow and continue — stdout/file logging is sufficient.
        }
#pragma warning restore CA1416
    }
}

builder.Logging.AddDebug();



// Response compression for reduced payload size (C-04)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/javascript", "text/css", "application/json", "image/svg+xml" });
});

// WebOptimizer for JS/CSS minification (C-04)
builder.Services.AddWebOptimizer(pipeline =>
{
    // Minify all JS files (except already-minified lib files)
    pipeline.MinifyJsFiles("js/**/*.js");

    // Minify all CSS files
    pipeline.MinifyCssFiles("css/**/*.css");
});

// Configure HTML encoder to allow Hebrew characters through as UTF-8
// instead of encoding them as HTML entities (&#x5D0; etc.)
builder.Services.AddSingleton(
    System.Text.Encodings.Web.HtmlEncoder.Create(
        System.Text.Unicode.UnicodeRanges.BasicLatin,
        System.Text.Unicode.UnicodeRanges.Hebrew,
        System.Text.Unicode.UnicodeRanges.GeneralPunctuation));

// Configure localization
// FF_HEBREW_DEFAULT feature flag (stored in DB, managed at /Owner/FeatureFlags):
//   false → en-US default, legacy-cookie middleware OFF (rollback path)
//   true  → he-IL default, legacy en-US cookies are auto-cleared on next visit
// Read once at startup via raw SQL because DefaultRequestCulture is configured BEFORE
// DI is built. Toggling the flag in the admin UI requires an app restart to take effect.
// See LegacyCultureCookieResetMiddleware + LanguageToggle for the .culture_explicit marker.
var hebrewDefaultEnabled = ReadHebrewDefaultFlagFromDb(builder.Configuration);

static bool ReadHebrewDefaultFlagFromDb(IConfiguration cfg)
{
    try
    {
        var cs = cfg.GetConnectionString("Default");
        if (string.IsNullOrEmpty(cs)) return false;
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IsEnabled FROM FeatureFlags WHERE Name = 'FF_HEBREW_DEFAULT' AND CompanyId IS NULL AND UserId IS NULL LIMIT 1";
        var result = cmd.ExecuteScalar();
        return result != null && result != DBNull.Value && Convert.ToInt64(result) != 0;
    }
    catch
    {
        // Fresh install: the FeatureFlags table may not exist yet before first migration.
        // Treat any failure as "disabled" so startup never breaks on this check.
        return false;
    }
}
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "en-US", "he-IL" };
    options.DefaultRequestCulture = new RequestCulture(hebrewDefaultEnabled ? "he-IL" : "en-US");
    options.SupportedCultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
    options.SupportedUICultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();

    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
    options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
    options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Auth/Login");
    options.Conventions.AllowAnonymousToPage("/Auth/Signup");
    options.Conventions.AllowAnonymousToPage("/Auth/GriffinSignup");
    options.Conventions.AllowAnonymousToPage("/Api/Signup/GetSignupOptions");
    options.Conventions.AllowAnonymousToPage("/Public/Chores");
    options.Conventions.AllowAnonymousToPage("/Public/OnDuty");
    options.Conventions.AllowAnonymousToPage("/Api/Telemetry");
})
.AddViewLocalization()
.AddDataAnnotationsLocalization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ILocalizationService, LocalizationService>();

// Multitenancy Phase 2: Register tenant resolver and company context
builder.Services.AddScoped<ITenantResolver, TenantResolver>();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();

// Section 12B: Startup validation — fail fast instead of cryptic runtime errors
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrEmpty(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is required. Check appsettings.json.");

var ownerEmail = builder.Configuration["Seeding:Owner:Email"];
if (string.IsNullOrEmpty(ownerEmail))
    throw new InvalidOperationException("Seeding:Owner:Email is required. Check appsettings.json.");

// Multitenancy Phase 2: Register CompanyId interceptor
builder.Services.AddSingleton<CompanyIdInterceptor>();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, opt) =>
{
    var interceptor = serviceProvider.GetRequiredService<CompanyIdInterceptor>();
    opt.UseSqlite(connectionString)
       .EnableDetailedErrors()
       .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
       .AddInterceptors(interceptor)
       .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Auth/Login";
        opt.LogoutPath = "/Auth/Logout";
        opt.AccessDeniedPath = "/AccessDenied";
        opt.Cookie.Name = "shiftmgr.auth";
        opt.ExpireTimeSpan = TimeSpan.FromDays(7);
        opt.SlidingExpiration = true;

        // ✅ SECURITY FIX: Enhanced cookie security settings
        opt.Cookie.HttpOnly = true; // Prevent XSS attacks from accessing cookie
        opt.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Use Secure flag when HTTPS is available
        opt.Cookie.SameSite = SameSiteMode.Lax; // Prevent CSRF attacks while allowing normal navigation

        // ✅ PHASE 18: Add auth required prompt when redirecting unauthorized users
        opt.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = async context =>
            {
                // SAFEGUARD: If cookie auth challenge fires on an /api/ route, return 401
                // instead of redirecting to the login page. API consumers expect JSON errors,
                // not HTML redirects. This is defense-in-depth — normally ApiAuthenticationMiddleware
                // (registered before UseAuthorization) handles /api/ auth. But if middleware ordering
                // ever breaks, this prevents a confusing 302 to /Auth/Login.
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                        title = "Unauthorized",
                        status = 401,
                        detail = "Authentication required. Provide a valid X-API-Key header.",
                        instance = context.Request.Path.ToString()
                    });
                    return;
                }

                // Add reason=authRequired query parameter to inform user why they're seeing login
                var returnUrl = context.Request.Path + context.Request.QueryString;
                context.Response.Redirect($"/Auth/Login?reason=authRequired&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Grant-based policies are handled dynamically by GrantPolicyProvider (Grant:* prefix).
    // Only auth-only policies remain here.

    // Public section policies — all authenticated users can view
    options.AddPolicy("CanViewChores", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("CanViewOnDuty", policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddHttpClient(); // Required for MailService
builder.Services.AddHttpClient("GriffinClient")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (builder.Configuration.GetValue<bool>("Griffin:SkipSslValidation", false))
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    });
// Data Protection: Persist keys to stable filesystem path (survives IIS app pool recycle, server migration)
// Keys are stored alongside the app so they're included in backup scope (fixes G-01, D-07)
var dataProtectionKeysPath = Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys");
Directory.CreateDirectory(dataProtectionKeysPath);
var dpBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("ShiftManager");

// D-04: Protect DataProtection keys at rest with DPAPI on Windows
if (OperatingSystem.IsWindows())
{
    dpBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
}
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IEmailConfigService, EmailConfigService>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IEmailApiLogService, EmailApiLogService>();
// Griffin ADFS services
builder.Services.AddScoped<IGriffinConfigService, GriffinConfigService>();
builder.Services.AddScoped<IGriffinService, GriffinService>();
builder.Services.AddScoped<IGriffinApiLogService, GriffinApiLogService>();
// C-06: Configure memory cache with more frequent expiration scanning to prevent unbounded growth
builder.Services.AddMemoryCache(options =>
{
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5); // Scan for expired entries every 5 min (default is 1 min)
    options.CompactionPercentage = 0.25; // Remove 25% of entries when compaction triggers
});

// Phase 2C: Performance Optimization - Caching Services
builder.Services.AddScoped<IShiftTypeCacheService, ShiftTypeCacheService>();
builder.Services.AddScoped<IShiftTypeSeedService, ShiftTypeSeedService>();
builder.Services.AddScoped<IAppConfigCacheService, AppConfigCacheService>();
builder.Services.AddScoped<ICompanyCacheService, CompanyCacheService>();

builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IDirectorService, DirectorService>();
builder.Services.AddScoped<ITraineeService, TraineeService>();
builder.Services.AddScoped<ICompanyFilterService, CompanyFilterService>();
builder.Services.AddScoped<IViewAsModeService, ViewAsModeService>();
builder.Services.AddScoped<IOwnerCompanySelectorService, OwnerCompanySelectorService>(); // Owner company selector service
builder.Services.AddScoped<IUserPreferenceService, UserPreferenceService>(); // ✅ PHASE 20: User preference service
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IAvatarService, AvatarService>();
builder.Services.AddScoped<IChoreService, ChoreService>();
builder.Services.AddScoped<IOnDutyService, OnDutyService>();
builder.Services.AddScoped<IDutyRotationService, DutyRotationService>();
builder.Services.AddScoped<IBusyUserService, BusyUserService>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddSingleton<IRateLimitingService, RateLimitingService>();
builder.Services.AddSingleton<IDeploymentExportService, DeploymentExportService>();
builder.Services.AddSingleton<IValidationService, ValidationService>();
builder.Services.AddScoped<ISecurityLogger, SecurityLogger>();

// Data Lifecycle Services
builder.Services.AddScoped<IArchiveService, ArchiveService>();
builder.Services.AddScoped<IPurgeService, PurgeService>();
builder.Services.AddScoped<IImportService, ImportService>();

// Feature Flag Service (UI Overhaul)
builder.Services.AddScoped<IFeatureFlagService, FeatureFlagService>();

// Client Telemetry Service (B-019, B-020, B-021) - Local observability for air-gapped environments
builder.Services.AddScoped<IClientTelemetryService, ClientTelemetryService>();

// v3.0 Organizational Hierarchy Services
builder.Services.AddScoped<IHierarchyService, HierarchyService>();
builder.Services.AddScoped<IJobTypeService, JobTypeService>();
builder.Services.AddScoped<IGrantService, GrantService>();
builder.Services.AddScoped<IGrantBackfillService, GrantBackfillService>();
builder.Services.AddScoped<IAreaPaletteService, AreaPaletteService>();
builder.Services.AddScoped<IPermissionSimulatorService, PermissionSimulatorService>();
builder.Services.AddScoped<IScopeFilterService, ScopeFilterService>(); // A-018: Scope-based data filtering

// B-018: Concurrent Edit Conflict Detection
builder.Services.AddScoped<IConcurrencyService, ConcurrencyService>();
builder.Services.AddScoped<IWidgetService, WidgetService>();
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddScoped<IQuickInfoConfigService, QuickInfoConfigService>();
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IShiftGroupingService, ShiftGroupingService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// v3.0 Grant Authorization
builder.Services.AddScoped<IAuthorizationHandler, GrantAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, GrantPolicyProvider>();

// Language Management Services
builder.Services.AddScoped<ICompanyLocalizationService, CompanyLocalizationService>();
builder.Services.AddScoped<ILanguageManagementService, LanguageManagementService>();

// Ops Console Scheduler Services
builder.Services.AddScoped<IShiftProgramService, ShiftProgramService>();
builder.Services.AddScoped<IMasterProgramService, MasterProgramService>();

// Schedule Export Service
builder.Services.AddScoped<IScheduleExportService, ScheduleExportService>();

// Vacation Approval Chain Service
builder.Services.AddScoped<IVacationApprovalService, VacationApprovalService>();

// Background email queue: emails are enqueued by MailService and sent by the processor
builder.Services.AddSingleton<EmailBackgroundQueue>();
builder.Services.AddHostedService<EmailBackgroundProcessor>();

// Phase 6: Daily Notification Background Service
builder.Services.AddHostedService<DailyNotificationJob>();

// Data Safety: Automated SQLite backup service (fixes C-02, E-07)
builder.Services.AddHostedService<DatabaseBackupService>();

// Stale Request Reaper: weekly cleanup of old Pending requests
builder.Services.AddHostedService<StaleRequestReaperJob>();

// Data Safety: Graceful shutdown handler — WAL checkpoint on IIS app pool recycle (fixes C-08)
builder.Services.AddSingleton<GracefulShutdownService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GracefulShutdownService>());

// My Team Calendars Services
builder.Services.AddScoped<TeamCalendarService>();
builder.Services.AddScoped<TeamCalendarEventAggregator>();

// Excel Calendars Services
builder.Services.AddScoped<IShiftCalendarService, ShiftCalendarService>();
builder.Services.AddScoped<IChoreTypeService, ChoreTypeService>();
builder.Services.AddScoped<IHomeTypeService, HomeTypeService>();
builder.Services.AddScoped<IUserDayNoteService, UserDayNoteService>();
builder.Services.AddScoped<ICalendarTextEntryService, CalendarTextEntryService>();

// SignalR for real-time calendar updates
builder.Services.AddSignalR();
builder.Services.AddScoped<ICalendarNotificationService, CalendarNotificationService>();

// API Layer Services
builder.Services.AddScoped<ShiftManager.Services.Api.UserApiService>();
builder.Services.AddScoped<ShiftManager.Services.Api.ShiftApiService>();
builder.Services.AddScoped<ShiftManager.Services.Api.TimeOffApiService>();
builder.Services.AddScoped<ShiftManager.Services.Api.NotificationApiService>();
builder.Services.AddScoped<ShiftManager.Services.Api.SwapRequestApiService>();
builder.Services.AddScoped<ShiftManager.Services.Api.ChoreApiService>();
builder.Services.AddScoped<ShiftManager.Services.Api.OnDutyApiService>();
builder.Services.AddScoped<ShiftManager.Services.Api.FeedbackApiService>();
builder.Services.AddScoped<UserDataExportService>(); // QA Item 65: User data export

// Hierarchy Settings Service — cascade settings resolution (Area → Molecule → Company)
builder.Services.AddScoped<IHierarchySettingsService, HierarchySettingsService>();

// Shift Assignment Service — JobType/ShiftGrouping-aware assignment + validation
builder.Services.AddScoped<IShiftAssignmentService, ShiftAssignmentService>();

// Setup Task Service — onboarding/setup task tracking
builder.Services.AddScoped<ISetupTaskService, SetupTaskService>();

// Friendship Service — user friendship management
builder.Services.AddScoped<IFriendshipService, FriendshipService>();

// Add Controllers for API endpoints
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Add health checks for container orchestration
// F-06: Separate liveness (process alive) from readiness (can serve traffic)
// Liveness (/health) should NOT include DB — a stalled DB shouldn't cause IIS to restart the process.
// Readiness (/ready) includes DB, disk, and memory checks.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: new[] { "ready" })
    .AddCheck<DiskSpaceHealthCheck>("disk_space", tags: new[] { "ready" })
    .AddCheck<MemoryHealthCheck>("memory", tags: new[] { "live", "ready" });

// B-026: Swagger/OpenAPI configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShiftManager API",
        Version = "v1",
        Description = "REST API for ShiftManager - Military Shift Scheduling System. " +
                      "Provides endpoints for managing shifts, users, time-off requests, notifications, and more.",
        Contact = new OpenApiContact
        {
            Name = "ShiftManager Support"
        }
    });

    // Add API key authentication to Swagger
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key authentication via X-API-Key header",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "ApiKeyScheme"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });

    // Include XML comments for better documentation
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// C-08: Track startup timing for diagnostics (large DB may take 5-10s)
var startupStopwatch = System.Diagnostics.Stopwatch.StartNew();

// Ensure DB exists and seed minimal data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Deployment Export: Phase 2 — restore database + avatars + feedback
    if (DeploymentExportService.PendingDataRestore)
    {
        var exportService = app.Services.GetRequiredService<IDeploymentExportService>();
        await exportService.RestoreDataAsync(app.Services);
        logger.LogInformation("Deployment restore Phase 2 completed");
    }

    // ============================================================
    // PRE-MIGRATION BACKUP (fixes H-02, H-03, E-05)
    // Copy app.db before running migrations to enable rollback
    // ============================================================
    {
        var connStr = app.Configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        var dbFilePath = DatabaseBackupService.ExtractDbPath(connStr);

        if (File.Exists(dbFilePath))
        {
            try
            {
                var backupDir = app.Configuration.GetValue<string>("Backup:Directory") ?? "Backups";
                Directory.CreateDirectory(backupDir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var preMigrationPath = Path.Combine(backupDir, $"app.db.pre-migration-{timestamp}");
                File.Copy(dbFilePath, preMigrationPath, overwrite: false);
                logger.LogInformation("Pre-migration backup created: {Path} ({SizeKB:F1} KB)",
                    preMigrationPath, new FileInfo(preMigrationPath).Length / 1024.0);

                // Clean up old pre-migration backups — keep only the 2 most recent
                try
                {
                    var preMigrationFiles = Directory.GetFiles(backupDir, "app.db.pre-migration-*")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .Skip(2)
                        .ToList();
                    foreach (var oldFile in preMigrationFiles)
                    {
                        oldFile.Delete();
                        logger.LogInformation("Cleaned up old pre-migration backup: {FileName}", oldFile.Name);
                    }
                }
                catch (Exception cleanupEx)
                {
                    logger.LogWarning(cleanupEx, "Failed to clean up old pre-migration backups");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create pre-migration backup. Proceeding with migration.");
            }
        }
    }

    // F-01: Ensure the database directory exists before migration (IIS deployment fix)
    // When appsettings.Production.json specifies an absolute path like C:\ShiftManager\Data\app.db,
    // SQLite cannot create the file if the directory doesn't exist, causing HTTP 500.30.
    {
        var connStr = app.Configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        var dbFilePath = DatabaseBackupService.ExtractDbPath(connStr);
        var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbFilePath));
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
            logger.LogInformation("Created database directory: {Path}", dbDir);
        }
    }

    try
    {
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database migration failed. The application cannot start. Error: {Message}", ex.Message);
        throw;
    }

    // ============================================================
    // SQLITE WAL MODE + BUSY TIMEOUT (fixes C-01)
    // WAL mode allows concurrent reads during writes.
    // busy_timeout prevents immediate SQLITE_BUSY errors under contention.
    // NOTE: busy_timeout is also applied via the connection string (Busy Timeout=5000)
    // which covers all connections. This PRAGMA is retained as defense-in-depth.
    // ============================================================
    try
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        using (var walCmd = connection.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            var result = await walCmd.ExecuteScalarAsync();
            logger.LogInformation("SQLite journal mode set to: {Mode}", result);
        }
        using (var busyCmd = connection.CreateCommand())
        {
            busyCmd.CommandText = "PRAGMA busy_timeout=5000;";
            await busyCmd.ExecuteNonQueryAsync();
            logger.LogInformation("SQLite busy_timeout set to 5000ms");
        }
        // G-05: Log SQLite version at startup for diagnostics
        using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.CommandText = "SELECT sqlite_version();";
            var sqliteVersion = await versionCmd.ExecuteScalarAsync();
            logger.LogInformation("SQLite version: {Version}", sqliteVersion);
        }
        await connection.CloseAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to configure SQLite WAL mode / busy_timeout");
    }

    // ============================================================
    // LOAD SEEDING CONFIGURATION FROM appsettings.json
    // ============================================================
    var seedingOptions = new ShiftManager.Configuration.SeedingOptions();
    app.Configuration.GetSection(ShiftManager.Configuration.SeedingOptions.SectionName).Bind(seedingOptions);

    // Validate owner password in production
    if (!app.Environment.IsDevelopment() && seedingOptions.Owner.Password == "admin123")
    {
        logger.LogWarning(
            "⚠️ SECURITY WARNING: Using default owner password in {Environment} environment. " +
            "Please set a secure password in appsettings.json under Seeding:Owner:Password",
            app.Environment.EnvironmentName);
    }

    // Also support environment variable override for backward compatibility
    var envPassword = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
    if (!string.IsNullOrEmpty(envPassword))
    {
        seedingOptions.Owner.Password = envPassword;
    }

    var seedDirectorPassword = Environment.GetEnvironmentVariable("SEED_DIRECTOR_PASSWORD") ?? "director123";
    var hasNewGrantMappings = false; // Set true when new RoleTemplateGrant mappings are seeded — triggers user grant re-provisioning

    // ============================================================
    // SEED V3 HIERARCHY FIRST (before creating users)
    // ============================================================

    // H-04: Seed GrantTypes — upsert-style (insert missing by Key, not all-or-nothing)
    {
        var grantTypes = ShiftManager.Data.SeedData.GrantTypeSeed.GetGrantTypes();
        var existingKeys = await db.GrantTypes.Select(g => g.Key).ToHashSetAsync();
        var newGrants = grantTypes.Where(g => !existingKeys.Contains(g.Key)).ToList();
        if (newGrants.Any())
        {
            // Clear hardcoded Ids so SQLite auto-generates them (avoids UNIQUE constraint on Id)
            foreach (var g in newGrants) g.Id = 0;
            db.GrantTypes.AddRange(newGrants);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} new grant types (total defined: {Total})", newGrants.Count, grantTypes.Count);
        }
    }

    // PRE-SEED: Rename system templates whose Key changed in seed (e.g., AlhutLead→Lead, AlhutDirector→Director).
    // Must run BEFORE the key-based upsert below, otherwise the upsert would insert duplicates.
    {
        var seedTemplates = ShiftManager.Data.SeedData.RoleTemplateSeed.GetRoleTemplates();
        var seedById = seedTemplates.ToDictionary(r => r.Id);
        var existingSystem = await db.RoleTemplates.Where(r => r.IsSystem).ToListAsync();

        var renameCount = 0;
        foreach (var existing in existingSystem)
        {
            if (!seedById.TryGetValue(existing.Id, out var seed)) continue;

            var changed = false;

            // Rename Key if it differs (e.g., "AlhutLead" → "Lead")
            if (existing.Key != seed.Key)
            {
                logger.LogInformation("Renaming system RoleTemplate ID={Id} Key: {OldKey} → {NewKey}", existing.Id, existing.Key, seed.Key);
                existing.Key = seed.Key;
                existing.NameKey = seed.NameKey;
                existing.DescriptionKey = seed.DescriptionKey;
                changed = true;
            }

            // Force-update DisplayName if seed value differs (handles "Alhut SL" → "Squad Leader" etc.)
            if (!string.IsNullOrEmpty(seed.DisplayNameEN) && existing.DisplayNameEN != seed.DisplayNameEN)
            {
                logger.LogInformation("Updating RoleTemplate ID={Id} DisplayNameEN: {Old} → {New}", existing.Id, existing.DisplayNameEN, seed.DisplayNameEN);
                existing.DisplayNameEN = seed.DisplayNameEN;
                changed = true;
            }
            if (!string.IsNullOrEmpty(seed.DisplayNameHE) && existing.DisplayNameHE != seed.DisplayNameHE)
            {
                existing.DisplayNameHE = seed.DisplayNameHE;
                changed = true;
            }

            if (changed) renameCount++;
        }

        if (renameCount > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Pre-seed: renamed/updated {Count} system role templates", renameCount);
        }
    }

    // H-04: Seed RoleTemplates — upsert-style (insert missing by Key, update existing with missing fields)
    {
        var roleTemplates = ShiftManager.Data.SeedData.RoleTemplateSeed.GetRoleTemplates();
        var seedByKey = roleTemplates.ToDictionary(r => r.Key);
        var existingTemplates = await db.RoleTemplates.ToListAsync();
        var existingKeys = existingTemplates.Select(r => r.Key).ToHashSet();
        var newTemplates = roleTemplates.Where(r => !existingKeys.Contains(r.Key)).ToList();
        if (newTemplates.Any())
        {
            db.RoleTemplates.AddRange(newTemplates);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} new role templates", newTemplates.Count);
        }

        // Sync DerivedUserRole, ScopeLevel, IsVisibleInSignup, and other fields from seed to existing templates
        // (handles the case where migration added columns but didn't populate existing rows correctly)
        var syncCount = 0;
        foreach (var existing in existingTemplates)
        {
            if (!seedByKey.TryGetValue(existing.Key, out var seed)) continue;
            var changed = false;

            if (!existing.DerivedUserRole.HasValue && seed.DerivedUserRole.HasValue)
            {
                existing.DerivedUserRole = seed.DerivedUserRole;
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.DescriptionKey) && !string.IsNullOrEmpty(seed.DescriptionKey))
            {
                existing.DescriptionKey = seed.DescriptionKey;
                changed = true;
            }
            if (existing.ScopeLevel == default && seed.ScopeLevel != default)
            {
                existing.ScopeLevel = seed.ScopeLevel;
                changed = true;
            }
            // IsVisibleInSignup: migration set default=true for all rows, but some system templates
            // (Owner, Trainee, Assigner) should be false — always sync from seed for system templates
            if (existing.IsSystem && existing.IsVisibleInSignup != seed.IsVisibleInSignup)
            {
                existing.IsVisibleInSignup = seed.IsVisibleInSignup;
                changed = true;
            }
            // DisplayNameEN/HE: sync from seed if not already set by admin
            if (string.IsNullOrEmpty(existing.DisplayNameEN) && !string.IsNullOrEmpty(seed.DisplayNameEN))
            {
                existing.DisplayNameEN = seed.DisplayNameEN;
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.DisplayNameHE) && !string.IsNullOrEmpty(seed.DisplayNameHE))
            {
                existing.DisplayNameHE = seed.DisplayNameHE;
                changed = true;
            }

            if (changed) syncCount++;
        }
        if (syncCount > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Synced {Count} existing role templates with missing seed data (DerivedUserRole, ScopeLevel)", syncCount);
        }

        // Seed grants for ALL templates (existing + new) to fill in any missing mappings
        // Dedup key includes TargetJobTypeId to support dual-JobType grants (e.g., BRDirector ApproveVacations for BR + Hakam)
        var roleTemplateGrants = ShiftManager.Data.SeedData.RoleTemplateSeed.GetRoleTemplateGrants();
        var existingMappings = await db.RoleTemplateGrants
            .Select(g => new { g.RoleTemplateId, g.GrantTypeId, g.TargetJobTypeId })
            .ToListAsync();
        var existingSet = existingMappings.Select(m => $"{m.RoleTemplateId}:{m.GrantTypeId}:{m.TargetJobTypeId?.ToString() ?? "null"}").ToHashSet();

        // Prevent re-seeding of sentinel grants that were already resolved to actual JobType IDs.
        // On re-runs, resolved grants have positive IDs (e.g. "2:22:5") while the seed still uses
        // sentinel IDs (e.g. "2:22:-1"). Add sentinel aliases so the dedup catches them.
        var sentinelMapRef = ShiftManager.Data.SeedData.RoleTemplateSeed.JobTypeSentinelMap;
        if (sentinelMapRef.Count > 0)
        {
            var sentJobTypeNames = sentinelMapRef.Values.ToHashSet();
            var resolvedJobTypeIds = await db.JobTypes
                .Where(jt => sentJobTypeNames.Contains(jt.Name) && jt.MoleculeId == null)
                .ToDictionaryAsync(jt => jt.Name, jt => jt.Id);

            // Build reverse map: actual JobType ID → sentinel ID
            var reverseSentinel = new Dictionary<int, int>();
            foreach (var (sentinel, name) in sentinelMapRef)
                if (resolvedJobTypeIds.TryGetValue(name, out var actualId))
                    reverseSentinel[actualId] = sentinel;

            // For each existing resolved grant, also add its sentinel alias to existingSet
            foreach (var m in existingMappings)
                if (m.TargetJobTypeId.HasValue && reverseSentinel.TryGetValue(m.TargetJobTypeId.Value, out var sentinelId))
                    existingSet.Add($"{m.RoleTemplateId}:{m.GrantTypeId}:{sentinelId}");
        }

        var newMappings = roleTemplateGrants
            .Where(g => !existingSet.Contains($"{g.RoleTemplateId}:{g.GrantTypeId}:{g.TargetJobTypeId?.ToString() ?? "null"}"))
            .ToList();
        hasNewGrantMappings = newMappings.Any();
        if (hasNewGrantMappings)
        {
            // Clear hardcoded Ids so SQLite auto-generates them (avoids UNIQUE constraint on Id)
            foreach (var m in newMappings) m.Id = 0;
            db.RoleTemplateGrants.AddRange(newMappings);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {MappingCount} new grant mappings", newMappings.Count);
        }

        // Reconcile mutable fields on existing role-template-grant mappings.
        // Insertion above only adds NEW rows; it never updates rows whose identity keys
        // (RoleTemplateId, GrantTypeId, TargetJobTypeId) already exist but whose ScopeMode /
        // UseOwnJobType / CanOwn / CanGive have since changed in the seed. Without this,
        // seed edits never propagate to the DB and back-fill produces grants with stale scope.
        // System templates (IsSystem = true) are the source of truth from code; admin-customized
        // non-system templates are left alone.
        var fullExisting = await db.RoleTemplateGrants
            .Include(g => g.RoleTemplate)
            .Where(g => g.RoleTemplate != null && g.RoleTemplate.IsSystem)
            .ToListAsync();
        var seedLookup = roleTemplateGrants.ToDictionary(
            g => $"{g.RoleTemplateId}:{g.GrantTypeId}:{g.TargetJobTypeId?.ToString() ?? "null"}",
            g => g);
        int reconciled = 0;
        foreach (var row in fullExisting)
        {
            var key = $"{row.RoleTemplateId}:{row.GrantTypeId}:{row.TargetJobTypeId?.ToString() ?? "null"}";
            if (!seedLookup.TryGetValue(key, out var seed)) continue;
            bool changed = false;
            if (row.ScopeMode != seed.ScopeMode) { row.ScopeMode = seed.ScopeMode; changed = true; }
            if (row.UseOwnJobType != seed.UseOwnJobType) { row.UseOwnJobType = seed.UseOwnJobType; changed = true; }
            if (row.CanOwn != seed.CanOwn) { row.CanOwn = seed.CanOwn; changed = true; }
            if (row.CanGive != seed.CanGive) { row.CanGive = seed.CanGive; changed = true; }
            if (changed) reconciled++;
        }
        if (reconciled > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Reconciled {Count} role-template-grant mappings (ScopeMode/UseOwnJobType/CanOwn/CanGive changes)", reconciled);
        }
    }

    // POST-SEED CLEANUP: Remove orphaned role templates (TextLead, TextDirector) after merge.
    // These templates were removed from the seed; remap any references to their merged counterparts.
    // Idempotent — only runs if the orphan keys still exist in the DB.
    {
        var orphanMergeMap = new Dictionary<string, string>
        {
            { "TextLead", "Lead" },         // TextLead users → Lead
            { "TextDirector", "Director" }   // TextDirector users → Director
        };

        var orphanKeys = orphanMergeMap.Keys.ToList();
        var orphanTemplates = await db.RoleTemplates
            .Where(r => orphanKeys.Contains(r.Key))
            .ToListAsync();

        if (orphanTemplates.Any())
        {
            // Resolve target template IDs
            var targetKeys = orphanMergeMap.Values.ToHashSet();
            var targetTemplates = await db.RoleTemplates
                .Where(r => targetKeys.Contains(r.Key))
                .ToDictionaryAsync(r => r.Key, r => r.Id);

            foreach (var orphan in orphanTemplates)
            {
                var targetKey = orphanMergeMap[orphan.Key];
                if (!targetTemplates.TryGetValue(targetKey, out var targetId))
                {
                    logger.LogWarning("Cannot clean up orphan template {Key} (ID={Id}): target template {TargetKey} not found",
                        orphan.Key, orphan.Id, targetKey);
                    continue;
                }

                logger.LogInformation("Merging orphan RoleTemplate {Key} (ID={OldId}) → {TargetKey} (ID={NewId})",
                    orphan.Key, orphan.Id, targetKey, targetId);

                // Remap AppUser.RoleTemplateId
                var usersToRemap = await db.Users.IgnoreQueryFilters()
                    .Where(u => u.RoleTemplateId == orphan.Id)
                    .ToListAsync();
                foreach (var user in usersToRemap)
                    user.RoleTemplateId = targetId;
                if (usersToRemap.Any())
                    logger.LogInformation("  Remapped {Count} AppUser.RoleTemplateId from {Old} to {New}", usersToRemap.Count, orphan.Id, targetId);

                // Remap UserRoleAssignment.RoleTemplateId
                var assignmentsToRemap = await db.UserRoleAssignments.IgnoreQueryFilters()
                    .Where(a => a.RoleTemplateId == orphan.Id)
                    .ToListAsync();
                foreach (var assignment in assignmentsToRemap)
                    assignment.RoleTemplateId = targetId;
                if (assignmentsToRemap.Any())
                    logger.LogInformation("  Remapped {Count} UserRoleAssignment.RoleTemplateId from {Old} to {New}", assignmentsToRemap.Count, orphan.Id, targetId);

                // Remap UserJoinRequest.RequestedRoleTemplateId
                var requestsToRemap = await db.UserJoinRequests.IgnoreQueryFilters()
                    .Where(r => r.RequestedRoleTemplateId == orphan.Id)
                    .ToListAsync();
                foreach (var request in requestsToRemap)
                    request.RequestedRoleTemplateId = targetId;
                if (requestsToRemap.Any())
                    logger.LogInformation("  Remapped {Count} UserJoinRequest.RequestedRoleTemplateId from {Old} to {New}", requestsToRemap.Count, orphan.Id, targetId);

                // Remap GriffinConfig.DefaultProvisionedRoleTemplateId
                var configsToRemap = await db.GriffinConfigs.IgnoreQueryFilters()
                    .Where(c => c.DefaultProvisionedRoleTemplateId == orphan.Id)
                    .ToListAsync();
                foreach (var config in configsToRemap)
                    config.DefaultProvisionedRoleTemplateId = targetId;
                if (configsToRemap.Any())
                    logger.LogInformation("  Remapped {Count} GriffinConfig.DefaultProvisionedRoleTemplateId from {Old} to {New}", configsToRemap.Count, orphan.Id, targetId);

                // Null out RoleAssignmentAudit references (FK with Restrict delete — would block removal)
                var auditsFrom = await db.RoleAssignmentAudits.IgnoreQueryFilters()
                    .Where(a => a.FromRoleTemplateId == orphan.Id)
                    .ToListAsync();
                foreach (var audit in auditsFrom)
                    audit.FromRoleTemplateId = targetId;

                var auditsTo = await db.RoleAssignmentAudits.IgnoreQueryFilters()
                    .Where(a => a.ToRoleTemplateId == orphan.Id)
                    .ToListAsync();
                foreach (var audit in auditsTo)
                    audit.ToRoleTemplateId = targetId;

                var auditCount = auditsFrom.Count + auditsTo.Count;
                if (auditCount > 0)
                    logger.LogInformation("  Remapped {Count} RoleAssignmentAudit references from {Old} to {New}", auditCount, orphan.Id, targetId);

                // Delete RoleTemplateGrants for the orphan
                var orphanGrants = await db.RoleTemplateGrants
                    .Where(g => g.RoleTemplateId == orphan.Id)
                    .ToListAsync();
                db.RoleTemplateGrants.RemoveRange(orphanGrants);
                if (orphanGrants.Any())
                    logger.LogInformation("  Deleted {Count} RoleTemplateGrants for orphan template {Key}", orphanGrants.Count, orphan.Key);

                // Delete RoleTemplateJobTypeLabels for the orphan
                var orphanLabels = await db.RoleTemplateJobTypeLabels
                    .Where(l => l.RoleTemplateId == orphan.Id)
                    .ToListAsync();
                db.RoleTemplateJobTypeLabels.RemoveRange(orphanLabels);
                if (orphanLabels.Any())
                    logger.LogInformation("  Deleted {Count} RoleTemplateJobTypeLabels for orphan template {Key}", orphanLabels.Count, orphan.Key);

                // Delete the orphan template itself
                db.RoleTemplates.Remove(orphan);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Orphan role template cleanup complete: processed {Count} templates", orphanTemplates.Count);
        }
    }

    // Seed Shifty Organization (Project → Area → Molecules → Companies including SystemAdmins)
    await ShiftManager.Data.SeedData.ShiftyOrganizationSeed.SeedAsync(db);

    // Resolve sentinel TargetJobTypeId values in RoleTemplateGrants.
    // Sentinels (negative IDs) map to deployment-specific JobType names (e.g., BR, Hakam).
    // Must run AFTER ShiftyOrganizationSeed which creates the JobTypes.
    {
        var sentinelMap = ShiftManager.Data.SeedData.RoleTemplateSeed.JobTypeSentinelMap;
        var sentinelGrants = await db.RoleTemplateGrants
            .Where(g => g.TargetJobTypeId != null && g.TargetJobTypeId < 0)
            .ToListAsync();

        if (sentinelGrants.Any())
        {
            // Build name→ID lookup for all referenced JobTypes
            var jobTypeNames = sentinelMap.Values.ToHashSet();
            // Use area-wide job types only (MoleculeId == null) — sentinel resolution targets
            // area-scoped types like BR and Hakam, not molecule-specific variants
            var jobTypeLookup = await db.JobTypes
                .Where(jt => jobTypeNames.Contains(jt.Name) && jt.MoleculeId == null)
                .ToDictionaryAsync(jt => jt.Name, jt => jt.Id);

            // Build set of already-resolved grants to detect duplicates on re-run
            var resolvedSet = await db.RoleTemplateGrants
                .Where(g => g.TargetJobTypeId != null && g.TargetJobTypeId > 0)
                .Select(g => $"{g.RoleTemplateId}:{g.GrantTypeId}:{g.TargetJobTypeId}")
                .ToListAsync();
            var resolvedExisting = new HashSet<string>(resolvedSet);

            var resolved = 0;
            var removed = 0;
            foreach (var grant in sentinelGrants)
            {
                if (sentinelMap.TryGetValue(grant.TargetJobTypeId!.Value, out var jobTypeName) &&
                    jobTypeLookup.TryGetValue(jobTypeName, out var actualId))
                {
                    var key = $"{grant.RoleTemplateId}:{grant.GrantTypeId}:{actualId}";
                    if (resolvedExisting.Contains(key))
                    {
                        // Already resolved in a previous run — remove the duplicate sentinel row
                        db.RoleTemplateGrants.Remove(grant);
                        removed++;
                    }
                    else
                    {
                        grant.TargetJobTypeId = actualId;
                        resolvedExisting.Add(key);
                        resolved++;
                    }
                }
                else
                {
                    // JobType doesn't exist in this deployment — remove the grant
                    db.RoleTemplateGrants.Remove(grant);
                    removed++;
                }
            }

            await db.SaveChangesAsync();
            if (resolved > 0)
                logger.LogInformation("Resolved {Count} sentinel TargetJobTypeId values to actual JobType IDs", resolved);
            if (removed > 0)
                logger.LogWarning("Removed {Count} RoleTemplateGrants with unresolvable JobType sentinels (deployment may not have these JobTypes)", removed);
        }
    }

    // ============================================================
    // CATCH-UP: Ensure System molecule, SystemAdmins, and HQ companies exist
    // (these were added to ShiftyOrganizationSeed after some DBs were already seeded)
    // ============================================================
    {
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Name == "190");
        if (area != null)
        {
            // 1. Ensure System molecule exists
            var systemMol = await db.Molecules.FirstOrDefaultAsync(m => m.Name == "System" && m.AreaId == area.Id);
            if (systemMol == null)
            {
                systemMol = new Molecule { AreaId = area.Id, Name = "System", DisplayName = "מערכת", Type = MoleculeType.System };
                db.Molecules.Add(systemMol);
                await db.SaveChangesAsync();
                logger.LogInformation("Catch-up: Created System molecule");
            }

            // 2. Ensure SystemAdmins company exists (under System molecule)
            if (!await db.Companies.AnyAsync(c => c.Name == "SystemAdmins" && c.MoleculeId == systemMol.Id))
            {
                // Check if SystemAdmins exists under a wrong/no molecule and fix it
                var orphanedSysAdmins = await db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Name == "SystemAdmins");
                if (orphanedSysAdmins != null)
                {
                    orphanedSysAdmins.MoleculeId = systemMol.Id;
                    logger.LogInformation("Catch-up: Linked orphaned SystemAdmins company to System molecule");
                }
                else
                {
                    db.Companies.Add(new Company { Name = "SystemAdmins", DisplayName = "מנהלי מערכת", Slug = "system-admins", MoleculeId = systemMol.Id });
                    logger.LogInformation("Catch-up: Created SystemAdmins company");
                }
                await db.SaveChangesAsync();
            }

            // 3. Ensure each non-System molecule has an HQ company (for Director/AreaAdmin signup)
            var allMolecules = await db.Molecules.Where(m => m.AreaId == area.Id && m.Type != MoleculeType.System).ToListAsync();
            var moleculesWithHQ = await db.Companies
                .Where(c => c.IsHeadquarters)
                .Select(c => c.MoleculeId)
                .ToHashSetAsync();

            var missingHQ = allMolecules.Where(m => !moleculesWithHQ.Contains(m.Id)).ToList();
            if (missingHQ.Count > 0)
            {
                foreach (var mol in missingHQ)
                {
                    db.Companies.Add(new Company
                    {
                        Name = "HQ",
                        DisplayName = "כלל צוותי",
                        Slug = $"hq-{mol.Name.ToLowerInvariant()}",
                        MoleculeId = mol.Id,
                        IsHeadquarters = true
                    });
                }
                await db.SaveChangesAsync();
                logger.LogInformation("Catch-up: Created {Count} missing HQ companies for molecules: {Names}",
                    missingHQ.Count, string.Join(", ", missingHQ.Select(m => m.Name)));
            }
        }
    }

    // ============================================================
    // SEED ADDITIONAL MOLECULES/COMPANIES FROM appsettings.json
    // ============================================================

    // Seed additional molecules from configuration
    foreach (var molConfig in seedingOptions.AdditionalMolecules)
    {
        if (string.IsNullOrWhiteSpace(molConfig.Name))
            continue;

        // Check if molecule already exists
        if (await db.Molecules.AnyAsync(m => m.Name == molConfig.Name))
        {
            logger.LogDebug("Molecule {Name} already exists, skipping", molConfig.Name);
            continue;
        }

        // Find the area
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Name == molConfig.AreaName);
        if (area == null)
        {
            logger.LogWarning("Area {AreaName} not found for molecule {MoleculeName}, skipping", molConfig.AreaName, molConfig.Name);
            continue;
        }

        // Parse molecule type
        if (!Enum.TryParse<MoleculeType>(molConfig.Type, true, out var moleculeType))
        {
            logger.LogWarning("Invalid molecule type {Type} for {Name}, defaulting to Workforce", molConfig.Type, molConfig.Name);
            moleculeType = MoleculeType.Workforce;
        }

        var molecule = new Molecule
        {
            AreaId = area.Id,
            Name = molConfig.Name,
            DisplayName = string.IsNullOrWhiteSpace(molConfig.DisplayName) ? molConfig.Name : molConfig.DisplayName,
            Type = moleculeType
        };
        db.Molecules.Add(molecule);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded additional molecule: {Name} ({Type})", molConfig.Name, moleculeType);
    }

    // Seed additional companies from configuration
    foreach (var compConfig in seedingOptions.AdditionalCompanies)
    {
        if (string.IsNullOrWhiteSpace(compConfig.Name))
            continue;

        // Find the molecule first (we need it for the uniqueness check)
        var molecule = await db.Molecules.FirstOrDefaultAsync(m => m.Name == compConfig.MoleculeName);
        if (molecule == null)
        {
            logger.LogWarning("Molecule {MoleculeName} not found for company {CompanyName}, skipping", compConfig.MoleculeName, compConfig.Name);
            continue;
        }

        // Check if company already exists IN THIS MOLECULE (same name can exist in different molecules)
        if (await db.Companies.AnyAsync(c => c.Name == compConfig.Name && c.MoleculeId == molecule.Id))
        {
            logger.LogDebug("Company {Name} already exists in molecule {Molecule}, skipping", compConfig.Name, compConfig.MoleculeName);
            continue;
        }

        var newCompany = new Company
        {
            MoleculeId = molecule.Id,
            Name = compConfig.Name,
            DisplayName = string.IsNullOrWhiteSpace(compConfig.DisplayName) ? compConfig.Name : compConfig.DisplayName,
            Slug = compConfig.Slug ?? compConfig.Name.ToLowerInvariant().Replace(" ", "-")
        };
        db.Companies.Add(newCompany);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded additional company: {Name} in molecule {Molecule}", compConfig.Name, compConfig.MoleculeName);
    }

    // Seed additional departments from configuration (for Tech molecules)
    foreach (var deptConfig in seedingOptions.AdditionalDepartments)
    {
        if (string.IsNullOrWhiteSpace(deptConfig.Name))
            continue;

        // Find the molecule first (we need it for the uniqueness check)
        var molecule = await db.Molecules.FirstOrDefaultAsync(m => m.Name == deptConfig.MoleculeName);
        if (molecule == null)
        {
            logger.LogWarning("Molecule {MoleculeName} not found for department {DepartmentName}, skipping", deptConfig.MoleculeName, deptConfig.Name);
            continue;
        }

        // Check if department already exists IN THIS MOLECULE
        if (await db.Departments.AnyAsync(d => d.Name == deptConfig.Name && d.MoleculeId == molecule.Id))
        {
            logger.LogDebug("Department {Name} already exists in molecule {Molecule}, skipping", deptConfig.Name, deptConfig.MoleculeName);
            continue;
        }

        var newDepartment = new Department
        {
            MoleculeId = molecule.Id,
            Name = deptConfig.Name,
            DisplayName = string.IsNullOrWhiteSpace(deptConfig.DisplayName) ? deptConfig.Name : deptConfig.DisplayName,
            IsActive = true
        };
        db.Departments.Add(newDepartment);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded additional department: {Name} in molecule {Molecule}", deptConfig.Name, deptConfig.MoleculeName);
    }

    // ============================================================
    // GET SYSTEM COMPANY FOR OWNER USER
    // ============================================================

    // Get SystemAdmins company from the hierarchy (created by ShiftyOrganizationSeed)
    var company = await db.Companies.FirstOrDefaultAsync(c => c.Name == "SystemAdmins");
    if (company == null)
    {
        // Fallback: get first company if SystemAdmins doesn't exist
        company = db.Companies.OrderBy(c => c.Id).First();
    }

    // SECURITY-AUDITED: All IgnoreQueryFilters() in this startup seeding block are SAFE — runs at app startup only, not user-facing
    // H-04: Shift types are now molecule-scoped (seeded by ShiftyOrganizationSeed)
    // No per-company shift type seeding needed here — skip legacy seed

    // H-04: Seed config — upsert-style (insert missing by Key per company)
    {
        var defaultConfigs = new[]
        {
            new AppConfig{ CompanyId = company.Id, Key = "RestHours", Value = "8" },
            new AppConfig{ CompanyId = company.Id, Key = "WeeklyHoursCap", Value = "40" },
            new AppConfig{ CompanyId = company.Id, Key = "GameEnabled", Value = "true" },
            new AppConfig{ CompanyId = company.Id, Key = "GameGridSize", Value = "6" },
            new AppConfig{ CompanyId = company.Id, Key = "GamePointsPer3Match", Value = "40" },
            new AppConfig{ CompanyId = company.Id, Key = "GamePointsPer4Match", Value = "100" },
            new AppConfig{ CompanyId = company.Id, Key = "GamePointsPer5PlusMatch", Value = "200" },
            new AppConfig{ CompanyId = company.Id, Key = "GameMegaComboMultiplier", Value = "2" },
            new AppConfig{ CompanyId = company.Id, Key = "GameMegaCombo3MatchMinLines", Value = "0" },
            new AppConfig{ CompanyId = company.Id, Key = "GameMegaCombo4MatchMinLines", Value = "2" },
            new AppConfig{ CompanyId = company.Id, Key = "GameMegaCombo5MatchMinLines", Value = "0" },
            new AppConfig{ CompanyId = company.Id, Key = "GameMilestones", Value = "1000,2500,5000,7500,10000,15000,20000" },
        };
        var existingKeys = db.Configs.IgnoreQueryFilters()
            .Where(c => c.CompanyId == company.Id)
            .Select(c => c.Key)
            .ToHashSet();
        var newConfigs = defaultConfigs.Where(c => !existingKeys.Contains(c.Key)).ToArray();
        if (newConfigs.Length > 0)
        {
            db.Configs.AddRange(newConfigs);
            await db.SaveChangesAsync();
        }
    }

    // Seed owner user (using configuration from appsettings.json)
    if (!db.Users.IgnoreQueryFilters().Any())
    {
        var (hash, salt) = PasswordHasher.CreateHash(seedingOptions.Owner.Password);
        db.Users.Add(new AppUser
        {
            CompanyId = company.Id,
            Email = seedingOptions.Owner.Email,
            DisplayName = seedingOptions.Owner.DisplayName,
            Role = UserRole.Owner,
            IsActive = true,
            HasCompletedOnboarding = true,
            PasswordHash = hash,
            PasswordSalt = salt
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Created owner user: {Email}", seedingOptions.Owner.Email);
    }
    else
    {
        // Ensure existing owner has onboarding completed and lockout cleared
        var ownerUser = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == seedingOptions.Owner.Email);
        if (ownerUser != null && (!ownerUser.HasCompletedOnboarding || ownerUser.FailedLoginAttempts > 0 || ownerUser.LockoutEnd != null))
        {
            ownerUser.HasCompletedOnboarding = true;
            ownerUser.FailedLoginAttempts = 0;
            ownerUser.LockoutEnd = null;
            await db.SaveChangesAsync();
            logger.LogInformation("Fixed owner user flags: {Email}", seedingOptions.Owner.Email);
        }
    }

    // Seed test Director user and companies (for QA)
    // Section 1E: Migrated from IConfiguration to IFeatureFlagService
    // Uses async since cache isn't warmed yet at seeding time (warming happens after seeding)
    var seedFlagService = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
    var enableDirectorRole = await seedFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnableDirectorRole);
    if (enableDirectorRole && app.Environment.IsDevelopment())
    {
        // Create second company if it doesn't exist
        if (db.Companies.Count() < 2)
        {
            var company2 = new Company { Name = "Test Corp", Slug = "test-corp", DisplayName = "Test Corporation" };
            db.Companies.Add(company2);
            await db.SaveChangesAsync();

            // Shift types are now molecule-scoped (seeded by ShiftyOrganizationSeed)
            // No per-company shift type seeding needed

            // Seed config for second company
            if (!db.Configs.IgnoreQueryFilters().Any(c => c.CompanyId == company2.Id))
            {
                db.Configs.AddRange(new[] {
                    new AppConfig{ CompanyId = company2.Id, Key = "RestHours", Value = "8" },
                    new AppConfig{ CompanyId = company2.Id, Key = "WeeklyHoursCap", Value = "40" },
                });
                await db.SaveChangesAsync();
            }
        }

        // Create Director user if doesn't exist
        if (!db.Users.IgnoreQueryFilters().Any(u => u.Role == UserRole.Director))
        {
            var (dirHash, dirSalt) = PasswordHasher.CreateHash(seedDirectorPassword);
            var director = new AppUser
            {
                CompanyId = company.Id,
                Email = "director@local",
                DisplayName = "Test Director",
                Role = UserRole.Director,
                IsActive = true,
                PasswordHash = dirHash,
                PasswordSalt = dirSalt
            };
            db.Users.Add(director);
            await db.SaveChangesAsync();

            // Assign Director to both companies
            var ownerUser = db.Users.IgnoreQueryFilters().First(u => u.Role == UserRole.Owner);
            var allCompanies = db.Companies.ToList();

            foreach (var comp in allCompanies)
            {
                if (!db.DirectorCompanies.Any(dc => dc.UserId == director.Id && dc.CompanyId == comp.Id))
                {
                    db.DirectorCompanies.Add(new DirectorCompany
                    {
                        UserId = director.Id,
                        CompanyId = comp.Id,
                        GrantedBy = ownerUser.Id,
                        GrantedAt = DateTime.UtcNow
                    });
                }
            }
            await db.SaveChangesAsync();
        }
    }

    // Seed Owner user's grants - GODMODE: ALL 132 grants at Project level
    var ownerUserForGrants = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Role == UserRole.Owner);
    if (ownerUserForGrants != null)
    {
        // Get all grant types
        var allGrantTypes = await db.GrantTypes.ToListAsync();

        // Get existing owner grants to avoid duplicates
        var existingOwnerGrants = await db.Grants
            .Where(g => g.UserId == ownerUserForGrants.Id)
            .Select(g => g.GrantTypeId)
            .ToHashSetAsync();

        // Get the project ID for project-scoped grants (if Shifty organization exists)
        var project = await db.Projects.OrderBy(p => p.Id).FirstOrDefaultAsync();

        foreach (var grantType in allGrantTypes)
        {
            if (!existingOwnerGrants.Contains(grantType.Id))
            {
                db.Grants.Add(new Grant
                {
                    UserId = ownerUserForGrants.Id,
                    GrantTypeId = grantType.Id,
                    ProjectId = project?.Id, // Project-level scope for godmode
                    CanOwn = true,
                    CanGive = true,
                    GrantedAt = DateTime.UtcNow,
                    IsAutoGrant = false,
                    Notes = "Owner godmode grant"
                });
            }
        }

        await db.SaveChangesAsync();
    }

    // ============================================================
    // SEED FEATURE FLAGS
    // ============================================================
    // H-04: Seed FeatureFlags — upsert-style (insert missing by Name)
    {
        var featureFlags = ShiftManager.Data.SeedData.FeatureFlagSeed.GetFeatureFlags();
        var existingNames = await db.FeatureFlags.Select(f => f.Name).ToHashSetAsync();
        var newFlags = featureFlags.Where(f => !existingNames.Contains(f.Name)).ToList();
        if (newFlags.Any())
        {
            db.FeatureFlags.AddRange(newFlags);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} new feature flags (total defined: {Total})", newFlags.Count, featureFlags.Count);
        }
    }

    // ============================================================
    // SEED ON-DUTY TYPE CONFIGS
    // ============================================================
    {
        var existingDutyTypes = await db.OnDutyTypeConfigs.Select(d => d.TypeValue).ToListAsync();
        foreach (var dt in ShiftManager.Data.SeedData.OnDutyTypeSeed.GetOnDutyTypes())
        {
            if (!existingDutyTypes.Contains(dt.TypeValue))
                db.OnDutyTypeConfigs.Add(dt);
        }
        await db.SaveChangesAsync();
    }

    // Seed test data for QA automation (legacy seeder)
    try
    {
        var seeder = new TestDataSeeder(db, scope.ServiceProvider.GetRequiredService<ILogger<TestDataSeeder>>());
        await seeder.SeedTestUsersAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while seeding test data");
    }

    // ============================================================
    // SEED E2E TEST DATA (B-041)
    // Seeds consistent test scenarios for Playwright UI testing
    // Only runs in Development/Test environments
    // ============================================================
    try
    {
        await ShiftManager.Data.SeedData.TestDataSeed.SeedTestDataAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while seeding E2E test data");
    }

    // ============================================================
    // SEED QA AUTOMATION TEST USERS
    // Creates all ~45 test users matching qa-automation TEST_USERS definitions
    // Only runs in Development/Test environments
    // ============================================================
    try
    {
        await ShiftManager.Data.SeedData.QaTestUserSeed.SeedAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while seeding QA test users");
    }

    // Repair grants for all test users with RoleTemplates (seeder creates users but doesn't assign role template grants)
    try
    {
        var grantService = scope.ServiceProvider.GetRequiredService<IGrantService>();
        var testUsers = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.RoleTemplateId != null && (u.Email.EndsWith("@test") || u.Email.EndsWith("@shifty.test")))
            .ToListAsync();
        foreach (var user in testUsers)
        {
            var repaired = await grantService.RepairUserGrantsAsync(user.Id);
            if (repaired > 0)
                logger.LogInformation("Repaired {Count} grants for test user {Email}", repaired, user.Email);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while repairing test user grants");
    }

    // Re-provision grants for existing users whose role templates gained new grants (e.g., EditChoreTypes for Lead/BRDirector/MoleculeAdmin).
    // Idempotent: RepairUserGrantsAsync only adds missing grants, skips already-provisioned ones.
    // Early-exit: only queries users if the seed just added new grant mappings.
    if (hasNewGrantMappings)
    {
        try
        {
            var grantService = scope.ServiceProvider.GetRequiredService<IGrantService>();
            var templateIdsToRepair = new[] { 2, 3, 7 }; // BRDirector, Lead, MoleculeAdmin
            var usersToRepair = await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive && u.RoleTemplateId.HasValue && templateIdsToRepair.Contains(u.RoleTemplateId.Value))
                .Where(u => !u.Email.EndsWith("@test") && !u.Email.EndsWith("@shifty.test")) // Skip test users (already handled above)
                .ToListAsync();
            var totalRepaired = 0;
            foreach (var user in usersToRepair)
            {
                var repaired = await grantService.RepairUserGrantsAsync(user.Id);
                if (repaired > 0)
                {
                    totalRepaired += repaired;
                    logger.LogInformation("Re-provisioned {Count} grants for user {Email} (template {TemplateId})", repaired, user.Email, user.RoleTemplateId);
                }
            }
            if (totalRepaired > 0)
                logger.LogInformation("Grant re-provisioning complete: {Total} grants added for {UserCount} users with templates [2,3,7]", totalRepaired, usersToRepair.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while re-provisioning grants for existing users");
        }
    }
}

// ============================================================
// WARM FEATURE FLAG CACHE (Section 1B)
// Loads all flags from DB into IMemoryCache so sync IsEnabled()
// works immediately without DB queries during request handling.
// ============================================================
using (var scope = app.Services.CreateScope())
{
    var flagService = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
    await flagService.WarmCacheAsync();
}

// C-08: Log startup duration for diagnostics
startupStopwatch.Stop();
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogInformation("Database migration + seeding completed in {ElapsedMs}ms", startupStopwatch.ElapsedMilliseconds);
    if (startupStopwatch.ElapsedMilliseconds > 5000)
        startupLogger.LogWarning("Slow startup detected ({ElapsedMs}ms). Consider pre-warming or optimizing seed checks.", startupStopwatch.ElapsedMilliseconds);
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseStatusCodePagesWithReExecute("/StatusCode/{0}");

    // B-026: Enable Swagger UI in development
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShiftManager API v1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/StatusCode/{0}");
    app.UseHsts();
}

// ✅ Only redirect to HTTPS when explicitly enabled (or in Production)
// Correlation ID for request tracing (fixes F-07)
app.UseMiddleware<ShiftManager.Middleware.CorrelationIdMiddleware>();

var enableHttps = app.Configuration.GetValue<bool>("EnableHttpsRedirection", !app.Environment.IsDevelopment());
if (enableHttps)
{
    // Custom HTTPS redirect that excludes /Auth/GriffinCallback.
    // Griffin (ADFS) may POST the callback over HTTP when the IdP hasn't migrated to HTTPS yet.
    // This exclusion is self-resolving: once Griffin migrates to HTTPS, this path will naturally use HTTPS.
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps
            && !context.Request.Path.StartsWithSegments("/Auth/GriffinCallback"))
        {
            var host = context.Request.Host;
            var url = $"https://{host}{context.Request.Path}{context.Request.QueryString}";
            context.Response.Redirect(url, permanent: false);
            return;
        }
        await next();
    });
}

// Response compression middleware (C-04) — before static files
app.UseResponseCompression();

// WebOptimizer middleware — minifies JS/CSS on-the-fly (C-04)
app.UseWebOptimizer();

// Security headers middleware — placed BEFORE UseStaticFiles so static files also get security headers
app.Use(async (context, next) =>
{
    // Prevent clickjacking attacks
    context.Response.Headers["X-Frame-Options"] = "DENY";

    // Prevent MIME type sniffing
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";

    // Control referrer information
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // Content Security Policy — unsafe-inline required for inline event handlers (onclick, onchange, etc.)
    string csp;
    if (app.Environment.IsDevelopment())
    {
        // Dev-only: allow Figma MCP capture tool
        csp = "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' https://mcp.figma.com; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https://mcp.figma.com; " +
            "font-src 'self'; " +
            "connect-src 'self' ws: wss: https://mcp.figma.com; " +
            "frame-ancestors 'none'";
    }
    else
    {
        csp = "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self' ws: wss:; " +
            "frame-ancestors 'none'";
    }
    context.Response.Headers["Content-Security-Policy"] = csp;

    // Remove potentially revealing server headers
    context.Response.Headers.Remove("Server");
    context.Response.Headers.Remove("X-Powered-By");
    context.Response.Headers.Remove("X-AspNet-Version");

    await next();
});

// Configure static file serving with explicit MIME types for offline reliability
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Ensure correct MIME types for CSS and JS files
        if (ctx.File.Name.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.ContentType = "text/css; charset=utf-8";
        }
        else if (ctx.File.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.ContentType = "application/javascript; charset=utf-8";
        }

        // Cache control: versioned assets (asp-append-version) can be cached longer
        // Non-versioned assets require revalidation
        var hasVersion = ctx.Context.Request.QueryString.HasValue &&
                         ctx.Context.Request.QueryString.Value?.Contains("v=") == true;
        ctx.Context.Response.Headers["Cache-Control"] = hasVersion
            ? "public, max-age=604800, immutable"  // 7 days for versioned assets
            : "public, must-revalidate, max-age=0";
    }
});
app.UseRouting();

// Add request logging middleware (must be after routing, before auth)
app.UseRequestLogging();

// Add request localization middleware
// Must run BEFORE UseRequestLocalization so that deleting the legacy cookie causes
// the configured DefaultRequestCulture to take effect on the same request.
if (hebrewDefaultEnabled)
{
    app.UseMiddleware<ShiftManager.Middleware.LegacyCultureCookieResetMiddleware>();
}
app.UseRequestLocalization();

// Griffin ADFS authentication (BEFORE CompanyContext)
// Sets HttpContext.User from griffin.token cookie if present
app.UseMiddleware<GriffinAuthenticationMiddleware>();

// Multitenancy Phase 2: Add company context middleware
app.UseMiddleware<CompanyContextMiddleware>();

// Authentication must come before API middleware so cookie auth is available
app.UseAuthentication();

// CRITICAL: ApiAuthenticationMiddleware MUST run BEFORE UseAuthorization().
// It reads X-API-Key from incoming /api/v1/* requests and sets HttpContext.User with
// ApiKey claims. Without this, UseAuthorization() sees [Authorize] on V1 controllers,
// finds no authenticated user (cookie auth finds no cookie), and triggers a 302 redirect
// to /Auth/Login — meaning ALL API key auth is silently broken.
//
// This middleware is a no-op for non-/api paths (short-circuits at line 28), so Razor Pages
// cookie auth is unaffected.
//
// ┌─────────────────────────────────────────────────────────────────────────┐
// │ FUTURE REFACTOR: Option B — Register as a proper AuthenticationScheme  │
// ├─────────────────────────────────────────────────────────────────────────┤
// │ The correct long-term approach is to replace this middleware with a    │
// │ registered ASP.NET Core authentication handler:                        │
// │                                                                        │
// │ 1. Create ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>  │
// │    - Move key lookup + validation into HandleAuthenticateAsync()        │
// │    - Return AuthenticateResult.Success with ClaimsPrincipal on valid key│
// │    - Return AuthenticateResult.Fail on invalid/missing key             │
// │                                                                        │
// │ 2. Implement HandleChallengeAsync (401 problem+json)                   │
// │    and HandleForbidAsync (403 problem+json) so the framework           │
// │    returns proper API error responses instead of cookie redirects.      │
// │                                                                        │
// │ 3. Register in Program.cs:                                             │
// │    builder.Services.AddAuthentication(CookieAuthDefaults.Scheme)       │
// │        .AddCookie(...)                                                 │
// │        .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>("ApiKey", ...) │
// │                                                                        │
// │ 4. Update all V1 controllers:                                          │
// │    [Authorize(AuthenticationSchemes = "ApiKey")]                        │
// │                                                                        │
// │ 5. Move scope-checking into a custom IAuthorizationHandler / policy    │
// │    so authentication (who are you?) is separate from authorization     │
// │    (what can you do?).                                                 │
// │                                                                        │
// │ Benefits: pipeline-position-independent, explicit scheme binding,      │
// │ proper challenge/forbid at the framework level, future-proof for       │
// │ adding JWT/OAuth schemes.                                              │
// │                                                                        │
// │ This middleware + the OnRedirectToLogin guard below are the interim    │
// │ fix (Option A) until that refactor is done.                            │
// └─────────────────────────────────────────────────────────────────────────┘
// Wrap API auth in exception handling so DB failures (e.g. SQLITE_BUSY) during
// API key lookup return problem+json instead of HTML. The main ApiExceptionMiddleware
// is registered later in the pipeline and no longer wraps this middleware.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ApiAuthExceptionGuard");
        logger.LogError(ex, "Unhandled exception during API authentication for {Path}", context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                title = "Internal Server Error",
                status = 500,
                detail = "An internal error occurred during authentication."
            });
        }
    }
});
app.UseMiddleware<ShiftManager.Middleware.ApiAuthenticationMiddleware>();

app.UseAuthorization();

// B-016: Cache-Control headers for API responses
// Ensures calendar data is not cached stale, other users see changes within 30 seconds
app.Use(async (context, next) =>
{
    // Set cache headers for API responses BEFORE the response starts streaming
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.OnStarting(() =>
        {
            // Only modify if response hasn't already set cache headers
            if (!context.Response.Headers.ContainsKey("Cache-Control"))
            {
                // No caching for API data - prevents stale calendar data
                context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                context.Response.Headers["Pragma"] = "no-cache";
                context.Response.Headers["Expires"] = "0";
            }
            return Task.CompletedTask;
        });
    }

    await next();
});

// B-027: UI Rate Limiting Middleware (for calendar, context, widget endpoints)
// Uses user ID or IP for rate limiting, separate from API key-based limits
app.UseMiddleware<ShiftManager.Middleware.RateLimitingMiddleware>();

// Configure HMAC secret for API key hashing (D-04)
var hmacSecret = builder.Configuration["ApiKeyHmacSecret"];
if (!string.IsNullOrEmpty(hmacSecret))
{
    ShiftManager.Middleware.ApiAuthenticationMiddleware.HmacSecret = hmacSecret;
}
else if (!app.Environment.IsDevelopment())
{
    // SECURITY FIX: Refuse to start with default HMAC secret in production (prevents cross-deployment API key forgery)
    throw new InvalidOperationException(
        "SECURITY: ApiKeyHmacSecret is not configured. Using default HMAC secret in non-development environment " +
        "allows cross-deployment API key forgery. Set 'ApiKeyHmacSecret' in appsettings.Production.json.");
}

// API Middleware (only for /api routes)
// NOTE: ApiAuthenticationMiddleware is registered earlier (before UseAuthorization) —
// see the CRITICAL comment above. Do NOT re-add it here.
app.UseMiddleware<ShiftManager.Middleware.ApiExceptionMiddleware>(); // B-028: Standardized error responses
app.UseMiddleware<ShiftManager.Middleware.ApiRequestLoggingMiddleware>();
app.UseMiddleware<ShiftManager.Middleware.ApiRateLimitingMiddleware>(); // API key-based rate limiting
app.MapControllers(); // Map API controllers

// NOTE: CORS not configured — app is deployed air-gapped; browser same-origin policy is sufficient.
// If deployment moves to public network, configure explicit CORS policy.

// ============================================================
// EXCEL CALENDAR REDIRECTS (when feature flags enabled)
// Redirects old calendar URLs to new Excel-style calendars
// Uses middleware to redirect before Razor Pages handles request
// ============================================================
app.Use(async (context, next) =>
{
    var flagService = context.RequestServices.GetRequiredService<IFeatureFlagService>();
    var excelCalendarsEnabled = flagService.IsEnabled(FeatureFlagSeed.Flags.ExcelCalendars);

    // Only redirect GET requests — POST requests must reach the original page's handlers
    // for CRUD operations (e.g., Calendar/Table POST handlers for shift assignment)
    if (excelCalendarsEnabled && HttpMethods.IsGet(context.Request.Method))
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        string? redirectTo = null;

        // Shifts calendar redirects
        if (flagService.IsEnabled(FeatureFlagSeed.Flags.ExcelCalendarShifts))
        {
            if (path == "/calendar/month" || path == "/calendar/week" ||
                path == "/calendar/day" || path == "/calendar/table")
            {
                redirectTo = "/Calendar/Shifts" + context.Request.QueryString;
            }
        }

        // Chores calendar redirects
        if (flagService.IsEnabled(FeatureFlagSeed.Flags.ExcelCalendarChores))
        {
            if (path == "/chores/calendar")
            {
                redirectTo = "/Calendar/Chores" + context.Request.QueryString;
            }
            // Only redirect authenticated users; anonymous users stay on Public page
            if (path == "/public/chores" && context.User.Identity?.IsAuthenticated == true)
            {
                redirectTo = "/Calendar/Chores" + context.Request.QueryString;
            }
        }

        // On-Call calendar redirects
        if (flagService.IsEnabled(FeatureFlagSeed.Flags.ExcelCalendarOnCall))
        {
            // Only redirect authenticated users; anonymous users stay on Public page
            if (path == "/public/onduty" && context.User.Identity?.IsAuthenticated == true)
            {
                redirectTo = "/Calendar/OnCall" + context.Request.QueryString;
            }
        }

        if (redirectTo != null)
        {
            context.Response.Redirect(redirectTo, permanent: false);
            return;
        }
    }

    await next();
});

app.MapRazorPages();

// SignalR hub for real-time calendar updates
app.MapHub<CalendarHub>("/hubs/calendar");

// Health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// H-02: Version tracking endpoint — returns app version for deployment verification
app.MapGet("/api/v1/version", () =>
{
    var assemblyVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
    // Support version.txt file written by Build-Release.ps1
    var versionFile = Path.Combine(AppContext.BaseDirectory, "version.txt");
    var buildVersion = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
    return Results.Ok(new
    {
        version = buildVersion ?? assemblyVersion,
        assemblyVersion,
        environment = app.Environment.IsDevelopment() ? app.Environment.EnvironmentName : "Production"
    });
}).AllowAnonymous();

// ============================================================
// STARTUP SAFETY CHECKS
// ============================================================
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

    // Data Protection key availability check (fixes G-01, D-07)
    try
    {
        var dpProvider = app.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = dpProvider.CreateProtector("startup-check");
        var testData = protector.Protect("test");
        protector.Unprotect(testData);
        startupLogger.LogInformation("Data Protection keys verified at: {Path}",
            Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys"));
    }
    catch (Exception ex)
    {
        startupLogger.LogCritical(ex, "DATA PROTECTION KEY FAILURE: Cannot encrypt/decrypt data. " +
            "Email configs and other encrypted data will be unreadable. " +
            "Check DataProtection-Keys directory at: {Path}",
            Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys"));
    }

    // H-05: Warn if DataProtection-Keys directory is empty or missing (critical for deployment migration)
    {
        var dpKeysDir = Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys");
        if (!Directory.Exists(dpKeysDir) || !Directory.GetFiles(dpKeysDir, "*.xml").Any())
        {
            startupLogger.LogWarning(
                "DEPLOYMENT WARNING: DataProtection-Keys directory is empty or missing at {Path}. " +
                "This means new encryption keys will be generated. All previously encrypted data " +
                "(email API keys, session cookies) from other deployments will be unreadable. " +
                "Include DataProtection-Keys in your backup and deployment package.", dpKeysDir);
        }
    }

    // SQLite network share detection (fixes G-05)
    {
        var connStr = app.Configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        var dbFilePath = DatabaseBackupService.ExtractDbPath(connStr);
        var fullDbPath = Path.GetFullPath(dbFilePath);

        if (fullDbPath.StartsWith(@"\\") || fullDbPath.StartsWith("//"))
        {
            startupLogger.LogCritical(
                "SQLITE ON NETWORK SHARE DETECTED: {Path}. " +
                "SQLite file locking is unreliable on network shares and can cause database corruption. " +
                "Move app.db to a local disk immediately.", fullDbPath);
        }
    }

    // Timezone policy assertion (fixes A-04)
    var localTz = TimeZoneInfo.Local;
    startupLogger.LogInformation("Server timezone: {TimeZone} (UTC offset: {Offset})",
        localTz.DisplayName, localTz.BaseUtcOffset);
    // Warn if server timezone doesn't match expected Israel timezone
    var expectedTzId = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
        ? "Israel Standard Time" : "Asia/Jerusalem";
    if (localTz.Id != expectedTzId && !localTz.DisplayName.Contains("Israel") && !localTz.DisplayName.Contains("Jerusalem"))
    {
        startupLogger.LogWarning(
            "Server timezone '{CurrentTZ}' does not match expected '{ExpectedTZ}'. " +
            "Date boundaries for shifts may be incorrect. Set server timezone to Israel Standard Time.",
            localTz.Id, expectedTzId);
    }

    // Default credentials warning (fixes D-01, B-06) — checked during seeding but also at startup banner level
    var seedingPassword = app.Configuration.GetValue<string>("Seeding:Owner:Password");
    if (seedingPassword == "admin123" && !app.Environment.IsDevelopment())
    {
        startupLogger.LogWarning(
            "DEFAULT CREDENTIALS: Owner account is using the default password 'admin123'. " +
            "Change immediately in production via appsettings.json Seeding:Owner:Password or SEED_ADMIN_PASSWORD env var.");
    }

    // AllowPublicSignup warning (fixes H-07, B-10)
    using (var flagScope = app.Services.CreateScope())
    {
        var flagService = flagScope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
        if (flagService.IsEnabled(FeatureFlagSeed.Flags.AllowPublicSignup) && !app.Environment.IsDevelopment())
        {
            startupLogger.LogWarning(
                "PUBLIC SIGNUP ENABLED: Anyone with access to this server can create an account. " +
                "Disable via Owner > Feature Flags or set Features:AllowPublicSignup=false in appsettings.json.");
        }
    }
}

// ============================================================
// STARTUP BANNER - Display application information
// ============================================================
DisplayStartupBanner(app);

app.Run();

}
catch (Exception ex)
{
    // F-01: Write fatal startup exception to file for IIS 500.30 diagnosis.
    // Under IIS in-process hosting, if the app crashes before logging is configured,
    // there's no way to see the error. This file is the last resort.
    var errorFile = Path.Combine(AppContext.BaseDirectory, "startup-error.txt");
    var errorMessage = $"""
        ShiftManager Fatal Startup Error
        ==================================
        Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
        Error: {ex.GetType().Name}: {ex.Message}

        Stack Trace:
        {ex}

        Inner Exception:
        {ex.InnerException}

        COMMON FIXES:
        1. Check that the 'logs' folder exists in the app directory
        2. Check that C:\ShiftManager\Data\ directory exists (or update ConnectionStrings:Default in appsettings.Production.json)
        3. Ensure the IIS App Pool identity has write permissions to the app folder
        4. Run UNBLOCK_FILES.bat if DLLs were copied via USB
        5. Check appsettings.Production.json has valid ApiKeyHmacSecret
        6. Check Windows Event Viewer > Application for additional details
        """;
    try { File.WriteAllText(errorFile, errorMessage); } catch { /* last resort failed */ }
    throw; // Re-throw so IIS ANCM reports the error
}

// ============================================================
// STARTUP BANNER METHOD
// ============================================================
void DisplayStartupBanner(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    
    // Get configuration values
    var emailEnabled = app.Configuration.GetValue<bool>("Email:Enabled");
    var griffinEnabled = app.Configuration.GetValue<bool>("Griffin:Enabled");

    // Feature flags from DB-backed service (warm cache loaded at startup)
    using var flagScope = app.Services.CreateScope();
    var flagService = flagScope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
    var publicSignup = flagService.IsEnabled(FeatureFlagSeed.Flags.AllowPublicSignup);
    var apiEnabled = flagService.IsEnabled(FeatureFlagSeed.Flags.ApiEnabled);
    var env = app.Environment.EnvironmentName;
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    
    // Get URLs - check multiple sources
    var urlList = new List<string>();
    
    // Try to get from server addresses (most reliable after app starts)
    if (app.Urls.Any())
    {
        urlList.AddRange(app.Urls);
    }
    else
    {
        // Fallback to configuration
        var configUrls = app.Configuration["ASPNETCORE_URLS"] 
            ?? app.Configuration["urls"] 
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? "http://localhost:5000;https://localhost:5001";
        urlList.AddRange(configUrls.Split(';', StringSplitOptions.RemoveEmptyEntries));
    }
    
    // Colors for console output
    var originalColor = Console.ForegroundColor;
    
    Console.WriteLine();
    
    // ASCII Banner - Cyan color
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"   _____ _     _  __ _   __  __                                    ");
    Console.WriteLine(@"  / ____| |   (_)/ _| | |  \/  |                                   ");
    Console.WriteLine(@" | (___ | |__  _| |_| |_| \  / | __ _ _ __   __ _  __ _  ___ _ __  ");
    Console.WriteLine(@"  \___ \| '_ \| |  _| __| |\/| |/ _` | '_ \ / _` |/ _` |/ _ \ '__| ");
    Console.WriteLine(@"  ____) | | | | | | | |_| |  | | (_| | | | | (_| | (_| |  __/ |    ");
    Console.WriteLine(@" |_____/|_| |_|_|_|  \__|_|  |_|\__,_|_| |_|\__,_|\__, |\___|_|    ");
    Console.WriteLine(@"                                                  __/ |           ");
    Console.WriteLine(@"                                                 |___/            ");
    Console.ForegroundColor = originalColor;
    
    Console.WriteLine();
    
    // Box width = 68 characters (including borders)
    const int boxWidth = 68;
    const int contentWidth = boxWidth - 4; // Minus "  | " and " |"
    
    void WriteBoxLine(string content)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  | ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(content.PadRight(contentWidth));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(" |");
        Console.ForegroundColor = originalColor;
    }
    
    void WriteBoxSeparator(char left = '+', char fill = '-', char right = '+')
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {left}{new string(fill, boxWidth - 2)}{right}");
        Console.ForegroundColor = originalColor;
    }
    
    // Top border
    WriteBoxSeparator();
    
    // Version and Environment line
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  | ");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("Version: ");
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write(version.PadRight(12));
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("  Environment: ");
    Console.ForegroundColor = env == "Development" ? ConsoleColor.Green : ConsoleColor.Magenta;
    Console.Write(env.PadRight(contentWidth - 39));
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine(" |");
    
    WriteBoxSeparator();
    
    // URLs section
    WriteBoxLine("Listening on:");
    foreach (var url in urlList)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  |   ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("> ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(url.PadRight(contentWidth - 4));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(" |");
    }
    
    WriteBoxSeparator();
    
    // Features section
    WriteBoxLine("Features:");
    
    // Feature rows - simpler fixed-width approach
    void WriteStatusLine(string line)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  | ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(line.PadRight(contentWidth));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(" |");
    }
    
    string FeatureStatus(string name, bool enabled) => 
        (enabled ? "[*] " : "[ ] ") + name;
    
    // Build feature strings
    var f1 = FeatureStatus("Email", emailEnabled).PadRight(20) + 
             FeatureStatus("Griffin ADFS", griffinEnabled).PadRight(22) + 
             FeatureStatus("REST API", apiEnabled);
    var f2 = FeatureStatus("Public Signup", publicSignup);
    
    WriteStatusLine(f1);
    WriteStatusLine(f2);

    // H-06: Log all feature flag states for version tracking (reads from DB-backed IFeatureFlagService)
    // Core flags emit a WARNING when disabled — these control production-critical features.
    // To change flag values: Owner > Feature Flags page (DB-backed, NOT appsettings).
    {
        var featureFlagLogger = app.Services.GetRequiredService<ILogger<Program>>();

        // Core flags — disabling these removes user-facing features; warn loudly
        var coreFlags = new[] {
            FeatureFlagSeed.Flags.ExcelCalendars,
            FeatureFlagSeed.Flags.ExcelCalendarShifts,
            FeatureFlagSeed.Flags.ExcelCalendarChores,
            FeatureFlagSeed.Flags.ExcelCalendarOnCall,
            FeatureFlagSeed.Flags.WidgetsEnabled,
            FeatureFlagSeed.Flags.EnableDirectorRole,
            FeatureFlagSeed.Flags.EnableDailyNotifications
        };

        // Operational flags — legitimately togglable per deployment; info-level only
        var operationalFlags = new[] {
            FeatureFlagSeed.Flags.AllowPublicSignup,
            FeatureFlagSeed.Flags.ApiEnabled
        };

        var disabledCoreFlags = new List<string>();
        foreach (var key in coreFlags)
        {
            var val = flagService.IsEnabled(key);
            featureFlagLogger.LogInformation("FeatureFlag: {Key}={Value}", key, val);
            if (!val) disabledCoreFlags.Add(key);
        }
        foreach (var key in operationalFlags)
        {
            var val = flagService.IsEnabled(key);
            featureFlagLogger.LogInformation("FeatureFlag: {Key}={Value}", key, val);
        }

        if (disabledCoreFlags.Count > 0)
        {
            featureFlagLogger.LogWarning(
                "DISABLED CORE FLAGS DETECTED: {Flags}. " +
                "These flags control production features and should normally be enabled. " +
                "To fix: log in as Owner > Feature Flags page, or delete app.db to re-seed defaults.",
                string.Join(", ", disabledCoreFlags));
        }
    }

    // Bottom border
    WriteBoxSeparator();
    
    Console.ForegroundColor = originalColor;
    Console.WriteLine();
    
    // Helpful tips
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  Press ");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("Ctrl+C");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write(" to shut down. Health check: ");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"{urlList.FirstOrDefault()}/health");
    Console.ForegroundColor = originalColor;
    
    Console.WriteLine();
    
    // Log the startup for structured logging as well
    logger.LogInformation(
        "ShiftManager started. Version={Version}, Environment={Environment}, URLs={Urls}, Email={EmailEnabled}, Griffin={GriffinEnabled}, API={ApiEnabled}",
        version, env, string.Join(";", urlList), emailEnabled, griffinEnabled, apiEnabled);
}

// Make Program accessible to integration tests
public partial class Program { }
