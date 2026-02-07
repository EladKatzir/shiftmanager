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
        builder.Logging.AddEventLog(settings =>
        {
            settings.SourceName = "ShiftManager";
            settings.LogName = "Application";
            settings.Filter = (category, level) => level >= LogLevel.Warning;
        });
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

// Configure localization
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "en-US", "he-IL" };
    options.DefaultRequestCulture = new RequestCulture("en-US");
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
})
.AddViewLocalization()
.AddDataAnnotationsLocalization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ILocalizationService, LocalizationService>();

// Multitenancy Phase 2: Register tenant resolver and company context
builder.Services.AddScoped<ITenantResolver, TenantResolver>();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();

// Multitenancy Phase 2: Register CompanyId interceptor
builder.Services.AddSingleton<CompanyIdInterceptor>();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, opt) =>
{
    var interceptor = serviceProvider.GetRequiredService<CompanyIdInterceptor>();
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default"))
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
            OnRedirectToLogin = context =>
            {
                // Add reason=authRequired query parameter to inform user why they're seeing login
                var returnUrl = context.Request.Path + context.Request.QueryString;
                context.Response.Redirect($"/Auth/Login?reason=authRequired&returnUrl={Uri.EscapeDataString(returnUrl)}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("IsManagerOrAdmin",
        policy => policy.RequireRole(nameof(UserRole.Manager), nameof(UserRole.Owner), nameof(UserRole.Director)));
    options.AddPolicy("IsAdmin", policy => policy.RequireRole(nameof(UserRole.Owner)));
    options.AddPolicy("IsDirector", policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Director)));
    options.AddPolicy("IsOwnerOrDirector", policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Director)));

    // Public section policies
    // View policies - all authenticated users can view
    options.AddPolicy("CanViewChores", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("CanViewOnDuty", policy => policy.RequireAuthenticatedUser());

    // Edit policies - only admin roles can edit
    options.AddPolicy("CanEditChores",
        policy => policy.RequireRole(nameof(UserRole.Manager), nameof(UserRole.Owner), nameof(UserRole.Director), nameof(UserRole.Assigner)));
    options.AddPolicy("CanEditOnDuty",
        policy => policy.RequireRole(nameof(UserRole.Manager), nameof(UserRole.Owner), nameof(UserRole.Director)));

    // Announcements management policy
    options.AddPolicy("CanManageAnnouncements", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole(nameof(UserRole.Owner)) ||
            context.User.IsInRole(nameof(UserRole.Director)) ||
            context.User.HasClaim("Grant", "ManageAnnouncements")));
});

builder.Services.AddHttpClient(); // Required for MailService
// Data Protection: Persist keys to stable filesystem path (survives IIS app pool recycle, server migration)
// Keys are stored alongside the app so they're included in backup scope (fixes G-01, D-07)
var dataProtectionKeysPath = Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("ShiftManager");
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IEmailConfigService, EmailConfigService>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IEmailApiLogService, EmailApiLogService>();
// Griffin ADFS services
builder.Services.AddScoped<IGriffinConfigService, GriffinConfigService>();
builder.Services.AddScoped<IGriffinService, GriffinService>();
builder.Services.AddScoped<IGriffinApiLogService, GriffinApiLogService>();
builder.Services.AddMemoryCache(); // For Griffin claims caching (may already be registered)

// Phase 2C: Performance Optimization - Caching Services
builder.Services.AddScoped<IShiftTypeCacheService, ShiftTypeCacheService>();
builder.Services.AddScoped<IAppConfigCacheService, AppConfigCacheService>();
builder.Services.AddScoped<ICompanyCacheService, CompanyCacheService>();

builder.Services.AddScoped<IConflictChecker, ConflictChecker>();
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
builder.Services.AddScoped<IScopeFilterService, ScopeFilterService>(); // A-018: Scope-based data filtering

// B-018: Concurrent Edit Conflict Detection
builder.Services.AddScoped<IConcurrencyService, ConcurrencyService>();
builder.Services.AddScoped<IWidgetService, WidgetService>();
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

// Tech Shift Services - Department-scoped tech shift eligibility and filtering
builder.Services.AddScoped<ITechShiftService, TechShiftService>();

// Phase 6: Daily Notification Background Service
builder.Services.AddHostedService<DailyNotificationJob>();

// Data Safety: Automated SQLite backup service (fixes C-02, E-07)
builder.Services.AddHostedService<DatabaseBackupService>();

// Data Safety: Graceful shutdown handler — WAL checkpoint on IIS app pool recycle (fixes C-08)
builder.Services.AddSingleton<GracefulShutdownService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GracefulShutdownService>());

// My Team Calendars Services
builder.Services.AddScoped<TeamCalendarService>();
builder.Services.AddScoped<TeamCalendarEventAggregator>();

// Excel Calendars Services
builder.Services.AddScoped<IShiftCalendarService, ShiftCalendarService>();
builder.Services.AddScoped<IChoreTypeService, ChoreTypeService>();
builder.Services.AddScoped<IUserDayNoteService, UserDayNoteService>();

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
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: new[] { "live", "ready" })
    .AddCheck("disk_space", new DiskSpaceHealthCheck(), tags: new[] { "ready" })
    .AddCheck("memory", new MemoryHealthCheck(), tags: new[] { "ready" });

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

// Ensure DB exists and seed minimal data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // ============================================================
    // PRE-MIGRATION BACKUP (fixes H-02, H-03, E-05)
    // Copy app.db before running migrations to enable rollback
    // ============================================================
    {
        var connStr = app.Configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        var dbFilePath = connStr.Split('=', 2).Length > 1 ? connStr.Split('=', 2)[1].Trim() : "app.db";

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
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create pre-migration backup. Proceeding with migration.");
            }
        }
    }

    await db.Database.MigrateAsync();

    // ============================================================
    // SQLITE WAL MODE + BUSY TIMEOUT (fixes C-01)
    // WAL mode allows concurrent reads during writes.
    // busy_timeout prevents immediate SQLITE_BUSY errors under contention.
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

    // ============================================================
    // SEED V3 HIERARCHY FIRST (before creating users)
    // ============================================================

    // Seed GrantTypes (107 grants)
    if (!await db.GrantTypes.AnyAsync())
    {
        var grantTypes = ShiftManager.Data.SeedData.GrantTypeSeed.GetGrantTypes();
        db.GrantTypes.AddRange(grantTypes);
        await db.SaveChangesAsync();
    }

    // Seed RoleTemplates (11 roles)
    if (!await db.RoleTemplates.AnyAsync())
    {
        var roleTemplates = ShiftManager.Data.SeedData.RoleTemplateSeed.GetRoleTemplates();
        db.RoleTemplates.AddRange(roleTemplates);
        await db.SaveChangesAsync();

        var roleTemplateGrants = ShiftManager.Data.SeedData.RoleTemplateSeed.GetRoleTemplateGrants();
        db.RoleTemplateGrants.AddRange(roleTemplateGrants);
        await db.SaveChangesAsync();
    }

    // Seed Shifty Organization (Project → Area → Molecules → Companies including SystemAdmins)
    await ShiftManager.Data.SeedData.ShiftyOrganizationSeed.SeedAsync(db);

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
        company = db.Companies.First();
    }

    // SECURITY-AUDITED: All IgnoreQueryFilters() in this startup seeding block are SAFE — runs at app startup only, not user-facing
    // Seed shift types (fixed keys) - company-specific
    if (!db.ShiftTypes.IgnoreQueryFilters().Any(st => st.CompanyId == company.Id))
    {
        db.ShiftTypes.AddRange(new[] {
            new ShiftType{ CompanyId=company.Id, Key="MORNING", Start=new TimeOnly(8,0), End=new TimeOnly(16,0)},
            new ShiftType{ CompanyId=company.Id, Key="NOON", Start=new TimeOnly(16,0), End=new TimeOnly(0,0)},
            new ShiftType{ CompanyId=company.Id, Key="NIGHT", Start=new TimeOnly(0,0), End=new TimeOnly(8,0)},
            new ShiftType{ CompanyId=company.Id, Key="MIDDLE", Start=new TimeOnly(12,0), End=new TimeOnly(20,0)},
            new ShiftType{ CompanyId=company.Id, Key="OFFLINE", Start=new TimeOnly(0,0), End=new TimeOnly(0,0)}, // Special shift type that can overlap
        });
        await db.SaveChangesAsync();
    }

    // Seed config
    if (!db.Configs.IgnoreQueryFilters().Any(c => c.CompanyId == company.Id))
    {
        db.Configs.AddRange(new[] {
            new AppConfig{ CompanyId = company.Id, Key = "RestHours", Value = "8" },
            new AppConfig{ CompanyId = company.Id, Key = "WeeklyHoursCap", Value = "40" },

            // Game Configuration Defaults
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
        });
        await db.SaveChangesAsync();
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
            PasswordHash = hash,
            PasswordSalt = salt
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Created owner user: {Email}", seedingOptions.Owner.Email);
    }

    // Seed test Director user and companies (for QA)
    var enableDirectorRole = app.Configuration.GetValue<bool>("Features:EnableDirectorRole", false);
    if (enableDirectorRole && app.Environment.IsDevelopment())
    {
        // Create second company if it doesn't exist
        if (db.Companies.Count() < 2)
        {
            var company2 = new Company { Name = "Test Corp", Slug = "test-corp", DisplayName = "Test Corporation" };
            db.Companies.Add(company2);
            await db.SaveChangesAsync();

            // Seed shift types for second company
            if (!db.ShiftTypes.IgnoreQueryFilters().Any(st => st.CompanyId == company2.Id))
            {
                db.ShiftTypes.AddRange(new[] {
                    new ShiftType{ CompanyId=company2.Id, Key="MORNING", Start=new TimeOnly(8,0), End=new TimeOnly(16,0)},
                    new ShiftType{ CompanyId=company2.Id, Key="NOON", Start=new TimeOnly(16,0), End=new TimeOnly(0,0)},
                    new ShiftType{ CompanyId=company2.Id, Key="NIGHT", Start=new TimeOnly(0,0), End=new TimeOnly(8,0)},
                    new ShiftType{ CompanyId=company2.Id, Key="MIDDLE", Start=new TimeOnly(12,0), End=new TimeOnly(20,0)},
                    new ShiftType{ CompanyId=company2.Id, Key="OFFLINE", Start=new TimeOnly(0,0), End=new TimeOnly(0,0)}, // Special shift type that can overlap
                });
                await db.SaveChangesAsync();
            }

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

    // Seed Owner user's grants - GODMODE: ALL 107 grants at Project level
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
        var project = await db.Projects.FirstOrDefaultAsync();

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
    if (!await db.FeatureFlags.AnyAsync())
    {
        var featureFlags = ShiftManager.Data.SeedData.FeatureFlagSeed.GetFeatureFlags();
        db.FeatureFlags.AddRange(featureFlags);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} feature flags", featureFlags.Count);
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
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseStatusCodePages("text/plain", "HTTP {0}");

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
    app.UseHsts();
}

// ✅ Only redirect to HTTPS when explicitly enabled (or in Production)
// Correlation ID for request tracing (fixes F-07)
app.UseMiddleware<ShiftManager.Middleware.CorrelationIdMiddleware>();

var enableHttps = app.Configuration.GetValue<bool>("EnableHttpsRedirection", !app.Environment.IsDevelopment());
if (enableHttps)
{
    app.UseHttpsRedirection();
}

// Response compression middleware (C-04) — before static files
app.UseResponseCompression();

// WebOptimizer middleware — minifies JS/CSS on-the-fly (C-04)
app.UseWebOptimizer();

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

// Security headers middleware with per-request CSP nonce (fixes G-02)
app.Use(async (context, next) =>
{
    // Generate per-request nonce for CSP (fixes G-02: replaces unsafe-inline for scripts)
    var nonceBytes = new byte[16];
    System.Security.Cryptography.RandomNumberGenerator.Fill(nonceBytes);
    var nonce = Convert.ToBase64String(nonceBytes);
    context.Items["CspNonce"] = nonce;

    // Prevent clickjacking attacks
    context.Response.Headers["X-Frame-Options"] = "DENY";

    // Prevent MIME type sniffing
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";

    // Control referrer information
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // CSP with nonce for inline scripts (still allows unsafe-inline for styles)
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}' 'unsafe-inline'; " + // Nonce + unsafe-inline fallback for older browsers
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'";

    // Remove potentially revealing server headers
    context.Response.Headers.Remove("Server");
    context.Response.Headers.Remove("X-Powered-By");
    context.Response.Headers.Remove("X-AspNet-Version");

    await next();
});

// Add request localization middleware
app.UseRequestLocalization();

// Griffin ADFS authentication (BEFORE CompanyContext)
// Sets HttpContext.User from griffin.token cookie if present
app.UseMiddleware<GriffinAuthenticationMiddleware>();

// Multitenancy Phase 2: Add company context middleware
app.UseMiddleware<CompanyContextMiddleware>();

// Authentication must come before API middleware so cookie auth is available
app.UseAuthentication();
app.UseAuthorization();

// B-016: Cache-Control headers for API responses
// Ensures calendar data is not cached stale, other users see changes within 30 seconds
app.Use(async (context, next) =>
{
    await next();

    // Set cache headers for API responses (after the response is generated)
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase))
    {
        // Only modify if response hasn't already set cache headers
        if (!context.Response.Headers.ContainsKey("Cache-Control"))
        {
            // No caching for API data - prevents stale calendar data
            context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
        }
    }
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

// API Middleware (only for /api routes)
app.UseMiddleware<ShiftManager.Middleware.ApiExceptionMiddleware>(); // B-028: Standardized error responses
app.UseMiddleware<ShiftManager.Middleware.ApiRequestLoggingMiddleware>();
app.UseMiddleware<ShiftManager.Middleware.ApiAuthenticationMiddleware>();
app.UseMiddleware<ShiftManager.Middleware.ApiRateLimitingMiddleware>(); // API key-based rate limiting
app.MapControllers(); // Map API controllers

// ============================================================
// EXCEL CALENDAR REDIRECTS (when feature flags enabled)
// Redirects old calendar URLs to new Excel-style calendars
// Uses middleware to redirect before Razor Pages handles request
// ============================================================
app.Use(async (context, next) =>
{
    var config = context.RequestServices.GetRequiredService<IConfiguration>();
    var excelCalendarsEnabled = config.GetValue<bool>("Features:ExcelCalendars");

    if (excelCalendarsEnabled)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        string? redirectTo = null;

        // Shifts calendar redirects
        if (config.GetValue<bool>("Features:ExcelCalendarShifts"))
        {
            if (path == "/calendar/month" || path == "/calendar/week" ||
                path == "/calendar/day" || path == "/calendar/table")
            {
                redirectTo = "/Calendar/Shifts" + context.Request.QueryString;
            }
        }

        // Chores calendar redirects
        if (config.GetValue<bool>("Features:ExcelCalendarChores"))
        {
            if (path == "/chores/calendar" || path == "/public/chores")
            {
                redirectTo = "/Calendar/Chores" + context.Request.QueryString;
            }
        }

        // On-Call calendar redirects
        if (config.GetValue<bool>("Features:ExcelCalendarOnCall"))
        {
            if (path == "/public/onduty")
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

    // SQLite network share detection (fixes G-05)
    {
        var connStr = app.Configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        var dbFilePath = connStr.Split('=', 2).Length > 1 ? connStr.Split('=', 2)[1].Trim() : "app.db";
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
    var publicSignup = app.Configuration.GetValue<bool>("Features:AllowPublicSignup");
    if (publicSignup && !app.Environment.IsDevelopment())
    {
        startupLogger.LogWarning(
            "PUBLIC SIGNUP ENABLED: Anyone with access to this server can create an account. " +
            "Set Features:AllowPublicSignup=false in appsettings.json for production deployments.");
    }
}

// ============================================================
// STARTUP BANNER - Display application information
// ============================================================
DisplayStartupBanner(app);

app.Run();

// ============================================================
// STARTUP BANNER METHOD
// ============================================================
void DisplayStartupBanner(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    
    // Get configuration values
    var emailEnabled = app.Configuration.GetValue<bool>("Email:Enabled");
    var griffinEnabled = app.Configuration.GetValue<bool>("Griffin:Enabled");
    var publicSignup = app.Configuration.GetValue<bool>("Features:AllowPublicSignup");
    var apiEnabled = app.Configuration.GetValue<bool>("Features:Api:Enabled");
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
