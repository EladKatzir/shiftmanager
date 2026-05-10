using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShiftManager.Models.Support;

// Source-generated LoggerMessage delegates for Program.cs (closes ~80 sites of CA1848,
// Batch K Phase 16 — FINAL phase). EventId range 90000-99999 reserved (10,000 IDs —
// startup events, intentionally distant from feature code). 75 unique methods cover
// 80 call sites; reuse of the ETM "Remap" template (Pattern C with {Entity} placeholder)
// and the catch-up creation template eliminates the 5 trivially-repeated sites.
//
// Sub-range allocations:
//   90000-90099: Database migration / pre-migration backup / DB directory  (10 methods)
//   90100-90199: SQLite WAL / busy_timeout / version PRAGMAs              ( 4 methods)
//   90200-90299: Grant type seeding                                       ( 1 method)
//   90300-90399: Role template seed/rename/sync/orphan cleanup            (12 methods)
//   90400-90499: Grant mappings + sentinel resolution + reconcile         ( 5 methods)
//   90500-90599: Catch-up (System molecule, SystemAdmins, HQ companies)   ( 4 methods)
//   90600-90699: Additional molecules/companies/departments seeding       ( 9 methods)
//   90700-90799: Owner / config seed                                      ( 3 methods)
//   90800-90899: Feature flags + test data seed + grant repair            (10 methods)
//   90900-90999: Role-scope migration / Home unification backfills        ( 6 methods)
//   91000-91099: Startup banner / lifecycle / safety checks               (12 methods)
//   91100-91199: Feature flag startup logging                             ( 2 methods)
//   91200-91299: Owner password warning + API auth exception              ( 2 methods)

public partial class Program
{
    // ── 90000-90099: Database migration / pre-migration backup / DB directory ──

    [LoggerMessage(EventId = 90000, Level = LogLevel.Information,
        Message = "Deployment restore Phase 2 completed")]
    private static partial void LogDeploymentRestoreCompleted(ILogger logger);

    [LoggerMessage(EventId = 90001, Level = LogLevel.Information,
        Message = "Pre-migration backup created: {Path} ({SizeKB:F1} KB)")]
    private static partial void LogPreMigrationBackupCreated(ILogger logger, string path, double sizeKB);

    [LoggerMessage(EventId = 90002, Level = LogLevel.Information,
        Message = "Cleaned up old pre-migration backup: {FileName}")]
    private static partial void LogPreMigrationBackupCleanedUp(ILogger logger, string fileName);

    [LoggerMessage(EventId = 90003, Level = LogLevel.Warning,
        Message = "Failed to clean up old pre-migration backups")]
    private static partial void LogPreMigrationCleanupFailed(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90004, Level = LogLevel.Warning,
        Message = "Failed to create pre-migration backup. Proceeding with migration.")]
    private static partial void LogPreMigrationBackupCreateFailed(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90005, Level = LogLevel.Information,
        Message = "Created database directory: {Path}")]
    private static partial void LogDbDirectoryCreated(ILogger logger, string path);

    [LoggerMessage(EventId = 90006, Level = LogLevel.Critical,
        Message = "Database migration failed. The application cannot start. Error: {Message}")]
    private static partial void LogDbMigrationFailed(ILogger logger, System.Exception ex, string message);

    // ── 90100-90199: SQLite WAL / busy_timeout / version PRAGMAs ──

    [LoggerMessage(EventId = 90100, Level = LogLevel.Information,
        Message = "SQLite journal mode set to: {Mode}")]
    private static partial void LogSqliteJournalMode(ILogger logger, object? mode);

    [LoggerMessage(EventId = 90101, Level = LogLevel.Information,
        Message = "SQLite busy_timeout set to 5000ms")]
    private static partial void LogSqliteBusyTimeout(ILogger logger);

    [LoggerMessage(EventId = 90102, Level = LogLevel.Information,
        Message = "SQLite version: {Version}")]
    private static partial void LogSqliteVersion(ILogger logger, object? version);

    [LoggerMessage(EventId = 90103, Level = LogLevel.Warning,
        Message = "Failed to configure SQLite WAL mode / busy_timeout")]
    private static partial void LogSqliteWalConfigFailed(ILogger logger, System.Exception ex);

    // ── 90200-90299: Grant type seeding ──

    [LoggerMessage(EventId = 90200, Level = LogLevel.Information,
        Message = "Seeded {Count} new grant types (total defined: {Total})")]
    private static partial void LogGrantTypesSeeded(ILogger logger, int count, int total);

    // ── 90300-90399: Role template seed / rename / sync / orphan cleanup ──

    [LoggerMessage(EventId = 90300, Level = LogLevel.Information,
        Message = "Renaming system RoleTemplate ID={Id} Key: {OldKey} → {NewKey}")]
    private static partial void LogRoleTemplateRenameKey(ILogger logger, int id, string oldKey, string newKey);

    [LoggerMessage(EventId = 90301, Level = LogLevel.Information,
        Message = "Updating RoleTemplate ID={Id} DisplayNameEN: {Old} → {New}")]
    private static partial void LogRoleTemplateUpdateDisplayNameEN(ILogger logger, int id, string? old, string? @new);

    [LoggerMessage(EventId = 90302, Level = LogLevel.Information,
        Message = "Pre-seed: renamed/updated {Count} system role templates")]
    private static partial void LogRoleTemplatePreSeedRenamed(ILogger logger, int count);

    [LoggerMessage(EventId = 90303, Level = LogLevel.Information,
        Message = "Seeded {Count} new role templates")]
    private static partial void LogRoleTemplatesSeeded(ILogger logger, int count);

    [LoggerMessage(EventId = 90304, Level = LogLevel.Information,
        Message = "Synced {Count} existing role templates with missing seed data (DerivedUserRole, ScopeLevel)")]
    private static partial void LogRoleTemplatesSynced(ILogger logger, int count);

    [LoggerMessage(EventId = 90310, Level = LogLevel.Warning,
        Message = "Cannot clean up orphan template {Key} (ID={Id}): target template {TargetKey} not found")]
    private static partial void LogOrphanTemplateMergeMissingTarget(ILogger logger, string key, int id, string targetKey);

    [LoggerMessage(EventId = 90311, Level = LogLevel.Information,
        Message = "Merging orphan RoleTemplate {Key} (ID={OldId}) → {TargetKey} (ID={NewId})")]
    private static partial void LogOrphanTemplateMerging(ILogger logger, string key, int oldId, string targetKey, int newId);

    // Reused pattern: "  Remapped {Count} {Entity} from {Old} to {New}" — 5 sites collapse to one method
    [LoggerMessage(EventId = 90312, Level = LogLevel.Information,
        Message = "  Remapped {Count} {Entity} from {Old} to {New}")]
    private static partial void LogOrphanTemplateRemapped(ILogger logger, int count, string entity, int old, int @new);

    [LoggerMessage(EventId = 90313, Level = LogLevel.Information,
        Message = "  Deleted {Count} RoleTemplateGrants for orphan template {Key}")]
    private static partial void LogOrphanTemplateGrantsDeleted(ILogger logger, int count, string key);

    [LoggerMessage(EventId = 90314, Level = LogLevel.Information,
        Message = "  Deleted {Count} RoleTemplateJobTypeLabels for orphan template {Key}")]
    private static partial void LogOrphanTemplateLabelsDeleted(ILogger logger, int count, string key);

    [LoggerMessage(EventId = 90315, Level = LogLevel.Information,
        Message = "Orphan role template cleanup complete: processed {Count} templates")]
    private static partial void LogOrphanTemplateCleanupComplete(ILogger logger, int count);

    // ── 90400-90499: Grant mappings + sentinel resolution + reconcile ──

    [LoggerMessage(EventId = 90400, Level = LogLevel.Information,
        Message = "Seeded {MappingCount} new grant mappings")]
    private static partial void LogGrantMappingsSeeded(ILogger logger, int mappingCount);

    [LoggerMessage(EventId = 90401, Level = LogLevel.Information,
        Message = "Reconciled {Count} role-template-grant mappings (ScopeMode/UseOwnJobType/CanOwn/CanGive changes)")]
    private static partial void LogGrantMappingsReconciled(ILogger logger, int count);

    [LoggerMessage(EventId = 90402, Level = LogLevel.Information,
        Message = "Resolved {Count} sentinel TargetJobTypeId values to actual JobType IDs")]
    private static partial void LogSentinelGrantsResolved(ILogger logger, int count);

    [LoggerMessage(EventId = 90403, Level = LogLevel.Warning,
        Message = "Removed {Count} RoleTemplateGrants with unresolvable JobType sentinels (deployment may not have these JobTypes)")]
    private static partial void LogSentinelGrantsRemoved(ILogger logger, int count);

    // ── 90500-90599: Catch-up (System molecule, SystemAdmins, HQ companies) ──

    [LoggerMessage(EventId = 90500, Level = LogLevel.Information,
        Message = "Catch-up: Created System molecule")]
    private static partial void LogCatchUpSystemMoleculeCreated(ILogger logger);

    [LoggerMessage(EventId = 90501, Level = LogLevel.Information,
        Message = "Catch-up: Linked orphaned SystemAdmins company to System molecule")]
    private static partial void LogCatchUpSystemAdminsLinked(ILogger logger);

    [LoggerMessage(EventId = 90502, Level = LogLevel.Information,
        Message = "Catch-up: Created SystemAdmins company")]
    private static partial void LogCatchUpSystemAdminsCreated(ILogger logger);

    [LoggerMessage(EventId = 90503, Level = LogLevel.Information,
        Message = "Catch-up: Created {Count} missing HQ companies for molecules: {Names}")]
    private static partial void LogCatchUpHqCompaniesCreated(ILogger logger, int count, string names);

    // ── 90600-90699: Additional molecules / companies / departments seeding ──

    [LoggerMessage(EventId = 90600, Level = LogLevel.Debug,
        Message = "Molecule {Name} already exists, skipping")]
    private static partial void LogAdditionalMoleculeExists(ILogger logger, string? name);

    [LoggerMessage(EventId = 90601, Level = LogLevel.Warning,
        Message = "Area {AreaName} not found for molecule {MoleculeName}, skipping")]
    private static partial void LogAdditionalMoleculeAreaMissing(ILogger logger, string? areaName, string? moleculeName);

    [LoggerMessage(EventId = 90602, Level = LogLevel.Warning,
        Message = "Invalid molecule type {Type} for {Name}, defaulting to Workforce")]
    private static partial void LogAdditionalMoleculeInvalidType(ILogger logger, string? type, string? name);

    [LoggerMessage(EventId = 90603, Level = LogLevel.Information,
        Message = "Seeded additional molecule: {Name} ({Type})")]
    private static partial void LogAdditionalMoleculeSeeded(ILogger logger, string? name, MoleculeType type);

    [LoggerMessage(EventId = 90610, Level = LogLevel.Warning,
        Message = "Molecule {MoleculeName} not found for company {CompanyName}, skipping")]
    private static partial void LogAdditionalCompanyMoleculeMissing(ILogger logger, string? moleculeName, string? companyName);

    [LoggerMessage(EventId = 90611, Level = LogLevel.Debug,
        Message = "Company {Name} already exists in molecule {Molecule}, skipping")]
    private static partial void LogAdditionalCompanyExists(ILogger logger, string? name, string? molecule);

    [LoggerMessage(EventId = 90612, Level = LogLevel.Information,
        Message = "Seeded additional company: {Name} in molecule {Molecule}")]
    private static partial void LogAdditionalCompanySeeded(ILogger logger, string? name, string? molecule);

    [LoggerMessage(EventId = 90620, Level = LogLevel.Warning,
        Message = "Molecule {MoleculeName} not found for department {DepartmentName}, skipping")]
    private static partial void LogAdditionalDepartmentMoleculeMissing(ILogger logger, string? moleculeName, string? departmentName);

    [LoggerMessage(EventId = 90621, Level = LogLevel.Debug,
        Message = "Department {Name} already exists in molecule {Molecule}, skipping")]
    private static partial void LogAdditionalDepartmentExists(ILogger logger, string? name, string? molecule);

    [LoggerMessage(EventId = 90622, Level = LogLevel.Information,
        Message = "Seeded additional department: {Name} in molecule {Molecule}")]
    private static partial void LogAdditionalDepartmentSeeded(ILogger logger, string? name, string? molecule);

    // ── 90700-90799: Owner / config seed ──

    [LoggerMessage(EventId = 90700, Level = LogLevel.Information,
        Message = "Created owner user: {Email}")]
    private static partial void LogOwnerCreated(ILogger logger, string email);

    [LoggerMessage(EventId = 90701, Level = LogLevel.Information,
        Message = "Fixed owner user flags: {Email}")]
    private static partial void LogOwnerFlagsFixed(ILogger logger, string email);

    // ── 90800-90899: Feature flags + test data seed + grant repair ──

    [LoggerMessage(EventId = 90800, Level = LogLevel.Information,
        Message = "Seeded {Count} new feature flags (total defined: {Total})")]
    private static partial void LogFeatureFlagsSeeded(ILogger logger, int count, int total);

    [LoggerMessage(EventId = 90810, Level = LogLevel.Error,
        Message = "An error occurred while seeding test data")]
    private static partial void LogSeedTestDataError(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90811, Level = LogLevel.Error,
        Message = "An error occurred while seeding E2E test data")]
    private static partial void LogSeedE2ETestDataError(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90812, Level = LogLevel.Error,
        Message = "An error occurred while seeding QA test users")]
    private static partial void LogSeedQaTestUsersError(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90820, Level = LogLevel.Information,
        Message = "Repaired {Count} grants for test user {Email}")]
    private static partial void LogTestUserGrantsRepaired(ILogger logger, int count, string email);

    [LoggerMessage(EventId = 90821, Level = LogLevel.Error,
        Message = "An error occurred while repairing test user grants")]
    private static partial void LogTestUserGrantRepairError(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90830, Level = LogLevel.Information,
        Message = "Re-provisioned {Count} grants for user {Email} (template {TemplateId})")]
    private static partial void LogUserGrantsReprovisioned(ILogger logger, int count, string email, int? templateId);

    [LoggerMessage(EventId = 90831, Level = LogLevel.Information,
        Message = "Grant re-provisioning complete: {Total} grants added for {UserCount} users with templates [2,3,7]")]
    private static partial void LogUserGrantReprovisioningComplete(ILogger logger, int total, int userCount);

    [LoggerMessage(EventId = 90832, Level = LogLevel.Error,
        Message = "An error occurred while re-provisioning grants for existing users")]
    private static partial void LogUserGrantReprovisioningError(ILogger logger, System.Exception ex);

    // ── 90900-90999: Role-scope migration / Home unification backfills ──

    [LoggerMessage(EventId = 90900, Level = LogLevel.Information,
        Message = "Role-scope migration: re-provisioned {GrantCount} grants for {UserCount} users (Kabar/Lead/Assigner chore+on-duty scope expansion)")]
    private static partial void LogRoleScopeMigrationComplete(ILogger logger, int grantCount, int userCount);

    [LoggerMessage(EventId = 90901, Level = LogLevel.Error,
        Message = "Role-scope migration failed (Kabar/Lead/Assigner chore policy)")]
    private static partial void LogRoleScopeMigrationFailed(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90910, Level = LogLevel.Information,
        Message = "Backfilled DerivedRotationRule.Anchor for {Count} legacy HomeTypes")]
    private static partial void LogDerivedRotationAnchorBackfilled(ILogger logger, int count);

    [LoggerMessage(EventId = 90911, Level = LogLevel.Error,
        Message = "DerivedRotationRule.Anchor backfill failed")]
    private static partial void LogDerivedRotationAnchorBackfillFailed(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90920, Level = LogLevel.Error,
        Message = "MoleculeApprovalSettings seed failed")]
    private static partial void LogMoleculeApprovalSettingsSeedFailed(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 90930, Level = LogLevel.Warning,
        Message = "Materialiser backfill failed for request {Id}")]
    private static partial void LogMaterialiserBackfillRequestFailed(ILogger logger, System.Exception ex, int id);

    [LoggerMessage(EventId = 90931, Level = LogLevel.Information,
        Message = "Materialiser backfill: {Succeeded} succeeded, {Failed} failed (of {Total})")]
    private static partial void LogMaterialiserBackfillSummary(ILogger logger, int succeeded, int failed, int total);

    [LoggerMessage(EventId = 90932, Level = LogLevel.Error,
        Message = "Materialiser backfill block failed")]
    private static partial void LogMaterialiserBackfillBlockFailed(ILogger logger, System.Exception ex);

    // ── 91000-91099: Startup banner / lifecycle / safety checks ──

    [LoggerMessage(EventId = 91000, Level = LogLevel.Information,
        Message = "Database migration + seeding completed in {ElapsedMs}ms")]
    private static partial void LogStartupSeedingCompleted(ILogger logger, long elapsedMs);

    [LoggerMessage(EventId = 91001, Level = LogLevel.Warning,
        Message = "Slow startup detected ({ElapsedMs}ms). Consider pre-warming or optimizing seed checks.")]
    private static partial void LogStartupSlow(ILogger logger, long elapsedMs);

    [LoggerMessage(EventId = 91010, Level = LogLevel.Information,
        Message = "Data Protection keys verified at: {Path}")]
    private static partial void LogDataProtectionKeysVerified(ILogger logger, string path);

    [LoggerMessage(EventId = 91011, Level = LogLevel.Critical,
        Message = "DATA PROTECTION KEY FAILURE: Cannot encrypt/decrypt data. Email configs and other encrypted data will be unreadable. Check DataProtection-Keys directory at: {Path}")]
    private static partial void LogDataProtectionKeyFailure(ILogger logger, System.Exception ex, string path);

    [LoggerMessage(EventId = 91012, Level = LogLevel.Warning,
        Message = "DEPLOYMENT WARNING: DataProtection-Keys directory is empty or missing at {Path}. This means new encryption keys will be generated. All previously encrypted data (email API keys, session cookies) from other deployments will be unreadable. Include DataProtection-Keys in your backup and deployment package.")]
    private static partial void LogDataProtectionKeysMissing(ILogger logger, string path);

    [LoggerMessage(EventId = 91020, Level = LogLevel.Critical,
        Message = "SQLITE ON NETWORK SHARE DETECTED: {Path}. SQLite file locking is unreliable on network shares and can cause database corruption. Move app.db to a local disk immediately.")]
    private static partial void LogSqliteNetworkShareDetected(ILogger logger, string path);

    [LoggerMessage(EventId = 91030, Level = LogLevel.Information,
        Message = "Server timezone: {TimeZone} (UTC offset: {Offset})")]
    private static partial void LogServerTimezone(ILogger logger, string timeZone, System.TimeSpan offset);

    [LoggerMessage(EventId = 91031, Level = LogLevel.Warning,
        Message = "Server timezone '{CurrentTZ}' does not match expected '{ExpectedTZ}'. Date boundaries for shifts may be incorrect. Set server timezone to Israel Standard Time.")]
    private static partial void LogServerTimezoneMismatch(ILogger logger, string currentTZ, string expectedTZ);

    [LoggerMessage(EventId = 91040, Level = LogLevel.Warning,
        Message = "PUBLIC SIGNUP ENABLED: Anyone with access to this server can create an account. Disable via Owner > Feature Flags or set Features:AllowPublicSignup=false in appsettings.json.")]
    private static partial void LogPublicSignupEnabledWarning(ILogger logger);

    [LoggerMessage(EventId = 91050, Level = LogLevel.Information,
        Message = "ShiftManager started. Version={Version}, Environment={Environment}, URLs={Urls}, Email={EmailEnabled}, Griffin={GriffinEnabled}, API={ApiEnabled}")]
    private static partial void LogStartupBanner(ILogger logger, string version, string environment, string urls, bool emailEnabled, bool griffinEnabled, bool apiEnabled);

    // ── 91100-91199: Feature flag startup logging ──

    [LoggerMessage(EventId = 91100, Level = LogLevel.Information,
        Message = "FeatureFlag: {Key}={Value}")]
    private static partial void LogFeatureFlagState(ILogger logger, string key, bool value);

    [LoggerMessage(EventId = 91101, Level = LogLevel.Warning,
        Message = "DISABLED CORE FLAGS DETECTED: {Flags}. These flags control production features and should normally be enabled. To fix: log in as Owner > Feature Flags page, or delete app.db to re-seed defaults.")]
    private static partial void LogDisabledCoreFlags(ILogger logger, string flags);

    // ── 91200-91299: Owner password warning + API auth exception ──

    [LoggerMessage(EventId = 91200, Level = LogLevel.Warning,
        Message = "⚠️ SECURITY WARNING: Using default owner password in {Environment} environment. Please set a secure password in appsettings.json under Seeding:Owner:Password")]
    private static partial void LogDefaultOwnerPasswordWarning(ILogger logger, string environment);

    [LoggerMessage(EventId = 91201, Level = LogLevel.Warning,
        Message = "DEFAULT CREDENTIALS: Owner account is using the default password 'admin123'. Change immediately in production via appsettings.json Seeding:Owner:Password or SEED_ADMIN_PASSWORD env var.")]
    private static partial void LogDefaultCredentialsBanner(ILogger logger);

    [LoggerMessage(EventId = 91210, Level = LogLevel.Error,
        Message = "Unhandled exception during API authentication for {Path}")]
    private static partial void LogApiAuthUnhandledException(ILogger logger, System.Exception ex, PathString path);
}
