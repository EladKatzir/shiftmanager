using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Pages;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Data Lifecycle - Archive, Purge, and Re-Import historical data
/// Owner-only access (NOT Director)
/// </summary>
[Authorize(Policy = "Grant:SystemConfiguration")]
public class DataLifecycleModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IArchiveService _archiveService;
    private readonly IPurgeService _purgeService;
    private readonly IImportService _importService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<DataLifecycleModel> _logger;
    private readonly IWebHostEnvironment _environment;

    private readonly ICompanyCacheService _companyCacheService;

    public DataLifecycleModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IArchiveService archiveService,
        IPurgeService purgeService,
        IImportService importService,
        ITenantResolver tenantResolver,
        IAuditLogService auditLogService,
        ILogger<DataLifecycleModel> logger,
        IWebHostEnvironment environment,
        ICompanyCacheService companyCacheService) : base(localizer)
    {
        _db = db;
        _archiveService = archiveService;
        _purgeService = purgeService;
        _importService = importService;
        _tenantResolver = tenantResolver;
        _auditLogService = auditLogService;
        _logger = logger;
        _environment = environment;
        _companyCacheService = companyCacheService;
    }

    // Tab 1: Create Archive
    [BindProperty] public DateOnly ArchiveCutoffDate { get; set; } = DateOnly.FromDateTime(DateTime.Now.AddMonths(-6));
    [BindProperty] public bool IncludeShifts { get; set; } = true;
    [BindProperty] public bool IncludeSwapRequests { get; set; } = true;
    [BindProperty] public bool IncludeTimeOff { get; set; } = true;
    [BindProperty] public bool IncludeChores { get; set; } = true;
    [BindProperty] public bool IncludeOnDuty { get; set; } = true;

    public ArchivePreview? Preview { get; set; }
    public ArchiveResult? LastArchive { get; set; }
    public ArchiveMetadata? LatestArchiveMetadata { get; set; }

    // Tab 2: Purge Data
    [BindProperty] public DateOnly PurgeCutoffDate { get; set; } = DateOnly.FromDateTime(DateTime.Now.AddMonths(-6));
    [BindProperty] public bool PurgeShifts { get; set; } = true;
    [BindProperty] public bool PurgeSwapRequests { get; set; } = true;
    [BindProperty] public bool PurgeTimeOff { get; set; } = true;
    [BindProperty] public bool PurgeChores { get; set; } = true;
    [BindProperty] public bool PurgeOnDuty { get; set; } = true;
    [BindProperty] public string TypedConfirmation { get; set; } = string.Empty;
    [BindProperty] public bool ArchiveConfirmed { get; set; }
    [BindProperty] public bool RunVacuum { get; set; } = true;
    [BindProperty] public bool HardDeleteOnDuty { get; set; }

    public bool HasFreshArchive { get; set; }
    public string ExpectedConfirmation { get; set; } = string.Empty;
    public PurgeResult? LastPurgeResult { get; set; }

    // Tab 3: Re-Import Archive
    [BindProperty] public IFormFile? UploadedArchive { get; set; }
    [BindProperty] public bool SkipMissingUsers { get; set; }
    [BindProperty] public ImportConflictPolicy ConflictPolicy { get; set; } = ImportConflictPolicy.SkipDuplicates;

    public ImportValidationResult? ValidationResult { get; set; }
    public ImportResult? LastImportResult { get; set; }
    public string? UploadedFilePath { get; set; }

    // General
    public string CompanyName { get; set; } = string.Empty;
    public string ActiveTab { get; set; } = "archive"; // Default tab

    public async Task OnGetAsync(string? tab = null)
    {
        try
        {
            ActiveTab = tab ?? "archive";

            // Get company name
            var companyId = _tenantResolver.GetCurrentTenantId();
            var company = await _companyCacheService.GetCompanyAsync(companyId);
            CompanyName = company?.Name ?? "Unknown";

            // Load latest archive metadata
            LatestArchiveMetadata = await _archiveService.GetLatestArchiveAsync();

            // Check for fresh archive (for purge tab)
            HasFreshArchive = await _archiveService.ValidateFreshArchiveAsync(PurgeCutoffDate, GetPurgeDataTypes());

            // Generate expected confirmation text
            ExpectedConfirmation = $"DELETE {CompanyName.ToUpperInvariant()} BEFORE {PurgeCutoffDate:yyyy-MM-dd}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading DataLifecycle page");
            TempData["ErrorMessage"] = "Failed to load page data."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }
    }

    #region Tab 1: Archive

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        try
        {
            var dataTypes = GetArchiveDataTypes();
            Preview = await _archiveService.PreviewArchiveAsync(ArchiveCutoffDate, dataTypes);

            var currentUserId = GetCurrentUserId();
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "ArchivePreviewGenerated",
                "DataArchive",
                null,
                $"Previewed archive: {Preview.Counts.Values.Sum()} records before {ArchiveCutoffDate}",
                JsonSerializer.Serialize(Preview));

            TempData["SuccessMessage"] = $"Preview generated: {Preview.Counts.Values.Sum()} total records, approximately {FormatBytes(Preview.EstimatedSizeBytes)}";
            ActiveTab = "archive";
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating preview");
            TempData["ErrorMessage"] = "Failed to generate preview. Please try again."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            ActiveTab = "archive";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCreateArchiveAsync()
    {
        try
        {
            var dataTypes = GetArchiveDataTypes();
            var request = new ArchiveRequest
            {
                CutoffDate = ArchiveCutoffDate,
                Types = dataTypes
            };

            LastArchive = await _archiveService.CreateArchiveAsync(request);

            if (LastArchive.Success)
            {
                TempData["SuccessMessage"] = $"Archive created successfully! CSV: {FormatBytes(LastArchive.CsvZipSizeBytes)}, Re-importable: {FormatBytes(LastArchive.NdjsonZipSizeBytes)}";
            }
            else
            {
                TempData["ErrorMessage"] = $"Archive creation failed: {LastArchive.Error}"; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            }

            ActiveTab = "archive";
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating archive");
            TempData["ErrorMessage"] = "Failed to create archive. Please try again."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            ActiveTab = "archive";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnGetDownloadCsvAsync()
    {
        try
        {
            var latest = await _archiveService.GetLatestArchiveAsync();
            if (latest == null)
            {
                return NotFound("No archive found.");
            }

            var csvZipPath = Path.Combine(_environment.ContentRootPath, "Archives", $"archive_csv_{latest.CompanyId}_{latest.CreatedAt:yyyyMMdd_HHmmss}.zip");

            if (!System.IO.File.Exists(csvZipPath))
            {
                return NotFound("Archive file not found.");
            }

            var currentUserId = GetCurrentUserId();
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "ArchiveDownloaded",
                "DataArchive",
                null,
                $"Downloaded CSV archive: {Path.GetFileName(csvZipPath)}",
                JsonSerializer.Serialize(new { fileName = Path.GetFileName(csvZipPath), zipType = "CSV" }));

            var fileBytes = await System.IO.File.ReadAllBytesAsync(csvZipPath);
            return File(fileBytes, "application/zip", Path.GetFileName(csvZipPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading CSV archive");
            TempData["ErrorMessage"] = "Failed to download archive. Please try again."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            ActiveTab = "archive";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnGetDownloadNdjsonAsync()
    {
        try
        {
            var latest = await _archiveService.GetLatestArchiveAsync();
            if (latest == null)
            {
                return NotFound("No archive found.");
            }

            var ndjsonZipPath = Path.Combine(_environment.ContentRootPath, "Archives", $"archive_import_{latest.CompanyId}_{latest.CreatedAt:yyyyMMdd_HHmmss}.zip");

            if (!System.IO.File.Exists(ndjsonZipPath))
            {
                return NotFound("Archive file not found.");
            }

            var currentUserId = GetCurrentUserId();
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "ArchiveDownloaded",
                "DataArchive",
                null,
                $"Downloaded NDJSON archive: {Path.GetFileName(ndjsonZipPath)}",
                JsonSerializer.Serialize(new { fileName = Path.GetFileName(ndjsonZipPath), zipType = "NDJSON" }));

            var fileBytes = await System.IO.File.ReadAllBytesAsync(ndjsonZipPath);
            return File(fileBytes, "application/zip", Path.GetFileName(ndjsonZipPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading NDJSON archive");
            TempData["ErrorMessage"] = "Failed to download archive. Please try again."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            ActiveTab = "archive";
            await OnGetAsync();
            return Page();
        }
    }

    #endregion

    #region Tab 2: Purge

    public async Task<IActionResult> OnPostPurgeAsync()
    {
        try
        {
            var dataTypes = GetPurgeDataTypes();
            var request = new PurgeRequest
            {
                CutoffDate = PurgeCutoffDate,
                Types = dataTypes,
                TypedConfirmation = TypedConfirmation,
                ArchiveConfirmed = ArchiveConfirmed,
                RunVacuum = RunVacuum,
                HardDeleteOnDuty = HardDeleteOnDuty
            };

            LastPurgeResult = await _purgeService.PurgeDataAsync(request);

            if (LastPurgeResult.Success)
            {
                var totalDeleted = LastPurgeResult.DeletedCounts.Values.Sum();
                var sizeSaved = LastPurgeResult.DbSizeBeforeBytes - LastPurgeResult.DbSizeAfterBytes;

                TempData["SuccessMessage"] = $"Purge completed! Deleted {totalDeleted} records. Database size reduced by {FormatBytes(sizeSaved)}. Backup: {Path.GetFileName(LastPurgeResult.BackupPath)}";
            }
            else
            {
                TempData["ErrorMessage"] = $"Purge failed: {LastPurgeResult.Error}"; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            }

            ActiveTab = "purge";
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during purge");
            TempData["ErrorMessage"] = "Purge failed. Please try again."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            ActiveTab = "purge";
            await OnGetAsync();
            return Page();
        }
    }

    #endregion

    #region Tab 3: Import

    public async Task<IActionResult> OnPostValidateArchiveAsync()
    {
        try
        {
            if (UploadedArchive == null || UploadedArchive.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select an archive file to upload."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                ActiveTab = "import";
                await OnGetAsync();
                return Page();
            }

            // Save uploaded file temporarily
            var uploadsDir = Path.Combine(_environment.ContentRootPath, "Uploads");
            Directory.CreateDirectory(uploadsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"temp_import_{timestamp}.zip";
            UploadedFilePath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(UploadedFilePath, FileMode.Create))
            {
                await UploadedArchive.CopyToAsync(stream);
            }

            // Validate archive
            ValidationResult = await _importService.ValidateArchiveAsync(UploadedFilePath);

            var currentUserId = GetCurrentUserId();
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "ArchiveValidated",
                "DataArchive",
                null,
                $"Validated archive for import: {ValidationResult.Errors.Count} errors, {ValidationResult.Warnings.Count} warnings",
                JsonSerializer.Serialize(ValidationResult));

            if (ValidationResult.IsValid)
            {
                TempData["SuccessMessage"] = $"Archive validated successfully! {ValidationResult.Warnings.Count} warning(s).";
            }
            else
            {
                TempData["ErrorMessage"] = $"Archive validation failed: {string.Join("; ", ValidationResult.Errors)}"; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            }

            // Store uploaded file path in TempData for import
            TempData["UploadedFilePath"] = UploadedFilePath;
            TempData["ValidationResultJson"] = JsonSerializer.Serialize(ValidationResult);

            ActiveTab = "import";
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating archive");
            TempData["ErrorMessage"] = "Failed to validate archive. Please try again."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            ActiveTab = "import";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        try
        {
            // Retrieve uploaded file path from TempData
            var uploadedFilePath = TempData["UploadedFilePath"]?.ToString();
            if (string.IsNullOrEmpty(uploadedFilePath) || !System.IO.File.Exists(uploadedFilePath))
            {
                TempData["ErrorMessage"] = "No validated archive found. Please validate the archive first."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                ActiveTab = "import";
                await OnGetAsync();
                return Page();
            }

            // Retrieve validation result
            var validationResultJson = TempData["ValidationResultJson"]?.ToString();
            if (!string.IsNullOrEmpty(validationResultJson))
            {
                ValidationResult = JsonSerializer.Deserialize<ImportValidationResult>(validationResultJson);
            }

            var request = new ImportRequest
            {
                ZipPath = uploadedFilePath,
                ConflictPolicy = ConflictPolicy,
                SkipMissingUsers = SkipMissingUsers
            };

            LastImportResult = await _importService.ImportArchiveAsync(request);

            if (LastImportResult.Success)
            {
                var totalInserted = LastImportResult.Stats.Values.Sum(s => s.Inserted);
                var totalSkipped = LastImportResult.Stats.Values.Sum(s => s.Skipped);
                var totalFailed = LastImportResult.Stats.Values.Sum(s => s.Failed);

                TempData["SuccessMessage"] = $"Import completed! Inserted: {totalInserted}, Skipped: {totalSkipped}, Failed: {totalFailed}. Duration: {LastImportResult.Duration.TotalSeconds:F1}s";
            }
            else
            {
                TempData["ErrorMessage"] = $"Import failed: {LastImportResult.Error}"; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            }

            // Clean up temporary file
            try
            {
                if (System.IO.File.Exists(uploadedFilePath))
                {
                    System.IO.File.Delete(uploadedFilePath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to delete temporary import file: {FilePath}", uploadedFilePath);
            }

            ActiveTab = "import";
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during import");
            TempData["ErrorMessage"] = "Import failed. Please try again."; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            ActiveTab = "import";
            await OnGetAsync();
            return Page();
        }
    }

    #endregion

    #region Helper Methods

    private ArchiveDataTypes GetArchiveDataTypes()
    {
        var types = ArchiveDataTypes.None;
        if (IncludeShifts) types |= ArchiveDataTypes.Shifts;
        if (IncludeSwapRequests) types |= ArchiveDataTypes.SwapRequests;
        if (IncludeTimeOff) types |= ArchiveDataTypes.TimeOff;
        if (IncludeChores) types |= ArchiveDataTypes.Chores;
        if (IncludeOnDuty) types |= ArchiveDataTypes.OnDuty;
        return types;
    }

    private ArchiveDataTypes GetPurgeDataTypes()
    {
        var types = ArchiveDataTypes.None;
        if (PurgeShifts) types |= ArchiveDataTypes.Shifts;
        if (PurgeSwapRequests) types |= ArchiveDataTypes.SwapRequests;
        if (PurgeTimeOff) types |= ArchiveDataTypes.TimeOff;
        if (PurgeChores) types |= ArchiveDataTypes.Chores;
        if (PurgeOnDuty) types |= ArchiveDataTypes.OnDuty;
        return types;
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    #endregion
}
