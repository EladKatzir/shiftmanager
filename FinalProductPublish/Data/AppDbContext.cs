using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ShiftManager.Models;
using ShiftManager.Models.Api;
using ShiftManager.Models.Support;
using ShiftManager.Models.Telemetry;
using ShiftManager.Services;

namespace ShiftManager.Data;

public class AppDbContext : DbContext
{
    private readonly ITenantResolver? _tenantResolver;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantResolver? tenantResolver = null)
        : base(options)
    {
        _tenantResolver = tenantResolver;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ShiftType> ShiftTypes => Set<ShiftType>();
    public DbSet<ShiftInstance> ShiftInstances => Set<ShiftInstance>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<TimeOffRequest> TimeOffRequests => Set<TimeOffRequest>();
    public DbSet<SwapRequest> SwapRequests => Set<SwapRequest>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<DailyNotificationPreference> DailyNotificationPreferences => Set<DailyNotificationPreference>();
    public DbSet<OnDutyRoleSubscription> OnDutyRoleSubscriptions => Set<OnDutyRoleSubscription>();
    public DbSet<AppConfig> Configs => Set<AppConfig>();
    public DbSet<DirectorCompany> DirectorCompanies => Set<DirectorCompany>();
    public DbSet<UserJoinRequest> UserJoinRequests => Set<UserJoinRequest>();
    public DbSet<RoleAssignmentAudit> RoleAssignmentAudits => Set<RoleAssignmentAudit>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProfileChangeAudit> ProfileChangeAudits => Set<ProfileChangeAudit>();
    public DbSet<Chore> Chores => Set<Chore>();
    public DbSet<ChoreType> ChoreTypes => Set<ChoreType>();
    public DbSet<ShiftCapacityOverride> ShiftCapacityOverrides => Set<ShiftCapacityOverride>();
    public DbSet<UserDayNote> UserDayNotes => Set<UserDayNote>();
    public DbSet<CalendarTextEntry> CalendarTextEntries => Set<CalendarTextEntry>();
    public DbSet<TeamCalendar> TeamCalendars => Set<TeamCalendar>();
    public DbSet<TeamCalendarMember> TeamCalendarMembers => Set<TeamCalendarMember>();
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();
    public DbSet<EmailApiLog> EmailApiLogs => Set<EmailApiLog>();
    public DbSet<EmailTemplateCustomization> EmailTemplateCustomizations => Set<EmailTemplateCustomization>();
    public DbSet<GriffinConfig> GriffinConfigs => Set<GriffinConfig>();
    public DbSet<GriffinApiLog> GriffinApiLogs => Set<GriffinApiLog>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<HomeType> HomeTypes => Set<HomeType>();
    public DbSet<HomeTypeOverride> HomeTypeOverrides => Set<HomeTypeOverride>();
    public DbSet<MoleculeApprovalSettings> MoleculeApprovalSettings => Set<MoleculeApprovalSettings>();

    // Justice Analytics (2026-05-03): configurable per-(work-type, scope) workload targets
    // for the Analytics page. CompanyId nullable; tenant filter follows EmailConfig pattern.
    public DbSet<JusticeTarget> JusticeTargets => Set<JusticeTarget>();

    // Language Management (tenant-scoped)
    public DbSet<CompanyLanguageSettings> CompanyLanguageSettings => Set<CompanyLanguageSettings>();
    public DbSet<CompanyLocalizationOverride> CompanyLocalizationOverrides => Set<CompanyLocalizationOverride>();

    // Ops Console Scheduler: Programs and Master Programs (tenant-scoped)
    public DbSet<ShiftProgram> ShiftPrograms => Set<ShiftProgram>();
    public DbSet<ProgramDay> ProgramDays => Set<ProgramDay>();
    public DbSet<MasterProgram> MasterPrograms => Set<MasterProgram>();
    public DbSet<MasterProgramItem> MasterProgramItems => Set<MasterProgramItem>();

    // Public/Global Tables (no CompanyId, visible across all tenancies)
    public DbSet<OnDuty> OnDuties => Set<OnDuty>();
    public DbSet<OnDutyTypeConfig> OnDutyTypeConfigs => Set<OnDutyTypeConfig>();

    // Store Hours & Quick Info Widget (area-scoped / molecule-scoped, no tenant filter)
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreHoursEntry> StoreHoursEntries => Set<StoreHoursEntry>();
    public DbSet<QuickInfoConfig> QuickInfoConfigs => Set<QuickInfoConfig>();

    // API Sidecar Tables (tenant-scoped via query filters + IBelongsToCompany)
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiKeyRequest> ApiKeyRequests => Set<ApiKeyRequest>();
    public DbSet<ApiRequestLog> ApiRequestLogs => Set<ApiRequestLog>();

    // ✅ PHASE 19: Game leaderboard scores (tenant-scoped)
    public DbSet<GameScore> GameScores => Set<GameScore>();

    // ========================================
    // v3.0 Organizational Hierarchy Entities
    // ========================================

    // Hierarchy (Project → Area → Molecule → Company/Department → User)
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<AreaCalendarPalette> AreaCalendarPalettes => Set<AreaCalendarPalette>();
    public DbSet<Molecule> Molecules => Set<Molecule>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobType> JobTypes => Set<JobType>();

    // Shift Groupings (Company + JobType combinations for shift scheduling)
    public DbSet<ShiftGrouping> ShiftGroupings => Set<ShiftGrouping>();
    public DbSet<ShiftGroupingCompany> ShiftGroupingCompanies => Set<ShiftGroupingCompany>();
    public DbSet<ShiftGroupingJobType> ShiftGroupingJobTypes => Set<ShiftGroupingJobType>();

    // Shift Categories (molecule-scoped functional grouping of shift elements + per-user membership)
    public DbSet<ShiftCategory> ShiftCategories => Set<ShiftCategory>();
    public DbSet<UserShiftCategory> UserShiftCategories => Set<UserShiftCategory>();

    // Draft Mode (per-assigner sandbox over a molecule+week of the shift calendar)
    public DbSet<DraftSession> DraftSessions => Set<DraftSession>();
    public DbSet<DraftCell> DraftCells => Set<DraftCell>();

    // Grant System (135 built-in grants as of 2026-05-23, 12 role templates)
    public DbSet<GrantType> GrantTypes => Set<GrantType>();
    public DbSet<Grant> Grants => Set<Grant>();
    public DbSet<RoleTemplate> RoleTemplates => Set<RoleTemplate>();
    public DbSet<RoleTemplateGrant> RoleTemplateGrants => Set<RoleTemplateGrant>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<RoleTemplateJobTypeLabel> RoleTemplateJobTypeLabels => Set<RoleTemplateJobTypeLabel>();

    // Settings Hierarchy (Area → Molecule → Company cascade)
    public DbSet<AreaSettings> AreaSettings => Set<AreaSettings>();
    public DbSet<MoleculeSettings> MoleculeSettings => Set<MoleculeSettings>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();

    // Circle/Friends System (cross-molecule visibility)
    public DbSet<UserFriendship> UserFriendships => Set<UserFriendship>();

    // Smart Task System (onboarding tasks)
    public DbSet<SetupTask> SetupTasks => Set<SetupTask>();

    // Feature Flags (UI Overhaul)
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    // ========================================
    // Client Telemetry (B-019, B-020, B-021)
    // Local observability for air-gapped environments
    // ========================================
    public DbSet<ClientAnalyticsEvent> ClientAnalyticsEvents => Set<ClientAnalyticsEvent>();
    public DbSet<ClientError> ClientErrors => Set<ClientError>();
    public DbSet<PerformanceMetric> PerformanceMetrics => Set<PerformanceMetric>();

    // ========================================
    // Duty Rotation System (global tables)
    // ========================================
    public DbSet<DutyRotation> DutyRotations => Set<DutyRotation>();
    public DbSet<DutyRotationEntry> DutyRotationEntries => Set<DutyRotationEntry>();
    public DbSet<DutyRotationLog> DutyRotationLogs => Set<DutyRotationLog>();

    // ========================================
    // Announcements Feed (tenant-scoped)
    // ========================================
    public DbSet<Announcement> Announcements => Set<Announcement>();

    // ========================================
    // Vacation Approval Rules (tenant-scoped)
    // ========================================
    public DbSet<VacationApprovalRule> VacationApprovalRules => Set<VacationApprovalRule>();

    // ========================================
    // Distribution Lists (molecule-scoped, shared user groups for calendar organization)
    // ========================================
    public DbSet<DistributionList> DistributionLists => Set<DistributionList>();
    public DbSet<DistributionListMember> DistributionListMembers => Set<DistributionListMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dateConverter = new ValueConverter<DateOnly, string>(
            v => v.ToString("yyyy-MM-dd"),
            v => SafeParseDateOnly(v));

        var timeConverter = new ValueConverter<TimeOnly, string>(
            v => v.ToString("HH:mm"),
            v => SafeParseTimeOnly(v));

        modelBuilder.Entity<ShiftType>()
            .Property(p => p.Start).HasConversion(timeConverter);
        modelBuilder.Entity<ShiftType>()
            .Property(p => p.End).HasConversion(timeConverter);

        modelBuilder.Entity<ShiftInstance>()
            .Property(p => p.WorkDate).HasConversion(dateConverter);

        modelBuilder.Entity<TimeOffRequest>()
            .Property(p => p.StartDate).HasConversion(dateConverter);
        modelBuilder.Entity<TimeOffRequest>()
            .Property(p => p.EndDate).HasConversion(dateConverter);

        // Profile Enhancements: AppUser DateOnly fields
        modelBuilder.Entity<AppUser>()
            .Property(p => p.DateOfBirth).HasConversion(dateConverter);
        modelBuilder.Entity<AppUser>()
            .Property(p => p.HireDate).HasConversion(dateConverter);

        // Chore timed assignments (SP1b: guard duty 10:00-14:00)
        modelBuilder.Entity<Chore>()
            .Property(p => p.StartTime).HasConversion(timeConverter);
        modelBuilder.Entity<Chore>()
            .Property(p => p.EndTime).HasConversion(timeConverter);

        // Phase 6: DailyNotificationPreference TimeOnly field
        modelBuilder.Entity<DailyNotificationPreference>()
            .Property(p => p.PreferredTime).HasConversion(timeConverter);

        modelBuilder.Entity<ShiftInstance>()
            .Property(p => p.Concurrency).IsConcurrencyToken();

        // CRITICAL: Add composite index for ShiftInstance date range queries
        // This index is used by ALL calendar views, dashboards, and reports
        modelBuilder.Entity<ShiftInstance>()
            .HasIndex(si => new { si.CompanyId, si.WorkDate });

        // Ops Console Scheduler: ShiftInstance relationship to ShiftProgram
        modelBuilder.Entity<ShiftInstance>()
            .HasOne(si => si.OriginalProgram)
            .WithMany()
            .HasForeignKey(si => si.OriginalProgramId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // Ops Console Scheduler: ShiftProgram indexes
        modelBuilder.Entity<ShiftProgram>()
            .HasIndex(p => new { p.CompanyId, p.ShiftTypeId });

        modelBuilder.Entity<ShiftProgram>()
            .HasIndex(p => new { p.CompanyId, p.IsActive });

        // Ops Console Scheduler: ProgramDay unique constraint (one entry per Program per DayOfWeek)
        modelBuilder.Entity<ProgramDay>()
            .HasIndex(pd => new { pd.ProgramId, pd.DayOfWeek })
            .IsUnique();

        // Ops Console Scheduler: MasterProgram index
        modelBuilder.Entity<MasterProgram>()
            .HasIndex(mp => new { mp.CompanyId, mp.IsActive });

        // Ops Console Scheduler: MasterProgramItem indexes
        modelBuilder.Entity<MasterProgramItem>()
            .HasIndex(mpi => new { mpi.MasterProgramId, mpi.ProgramId })
            .IsUnique();

        modelBuilder.Entity<MasterProgramItem>()
            .HasIndex(mpi => new { mpi.MasterProgramId, mpi.SortOrder });

        // Multitenancy Phase 1: Add unique index on Company.Slug for routing
        modelBuilder.Entity<Company>()
            .HasIndex(c => c.Slug).IsUnique();

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email).IsUnique();

        // ShiftType indexes for scope-based queries
        modelBuilder.Entity<ShiftType>()
            .HasIndex(s => new { s.CompanyId, s.Key })
            .HasFilter("CompanyId IS NOT NULL");

        // Prevent duplicate ShiftType Keys within the same molecule+jobType scope
        modelBuilder.Entity<ShiftType>()
            .HasIndex(s => new { s.MoleculeId, s.JobTypeId, s.Key })
            .IsUnique()
            .HasFilter("MoleculeId IS NOT NULL");

        // Scope-based lookup indexes
        modelBuilder.Entity<ShiftType>()
            .HasIndex(s => new { s.Scope, s.MoleculeId });
        modelBuilder.Entity<ShiftType>()
            .HasIndex(s => new { s.Scope, s.AreaId });

        // CHECK constraints: scope consistency
        modelBuilder.Entity<ShiftType>()
            .ToTable(t =>
            {
                t.HasCheckConstraint("CK_ShiftType_Company_Scope", "Scope != 0 OR CompanyId IS NOT NULL");
                t.HasCheckConstraint("CK_ShiftType_Area_Scope", "Scope != 2 OR AreaId IS NOT NULL");
                t.HasCheckConstraint("CK_ShiftType_Molecule_Scope", "Scope != 1 OR MoleculeId IS NOT NULL");
            });

        // Multitenancy Phase 1: Update ShiftAssignment index to include CompanyId
        modelBuilder.Entity<ShiftAssignment>()
            .HasIndex(a => new { a.CompanyId, a.ShiftInstanceId, a.UserId }).IsUnique();

        // HOME Unification: Link shifts to their source TimeOffRequest
        modelBuilder.Entity<ShiftAssignment>()
            .HasOne(sa => sa.SourceTimeOffRequest)
            .WithMany()
            .HasForeignKey(sa => sa.SourceTimeOffRequestId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ShiftAssignment>()
            .HasIndex(sa => sa.SourceTimeOffRequestId)
            .HasDatabaseName("IX_ShiftAssignments_SourceTimeOffRequestId")
            .HasFilter("[SourceTimeOffRequestId] IS NOT NULL");

        // Multitenancy Phase 1: Add composite index for TimeOffRequests
        modelBuilder.Entity<TimeOffRequest>()
            .HasIndex(t => new { t.CompanyId, t.UserId, t.StartDate });

        // HOME unification: per-molecule approval settings
        modelBuilder.Entity<MoleculeApprovalSettings>(b => {
            b.HasOne(m => m.Molecule)
                .WithMany()
                .HasForeignKey(m => m.MoleculeId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.UpdatedBy)
                .WithMany()
                .HasForeignKey(m => m.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(m => m.MoleculeId).IsUnique();
        });

        // HOME unification: dual-approval actor tracking
        modelBuilder.Entity<TimeOffRequest>()
            .HasOne(t => t.FirstApprovalActor)
            .WithMany()
            .HasForeignKey(t => t.FirstApprovalActorId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<TimeOffRequest>()
            .HasOne(t => t.SecondApprovalActor)
            .WithMany()
            .HasForeignKey(t => t.SecondApprovalActorId)
            .OnDelete(DeleteBehavior.SetNull);

        // Multitenancy Phase 1: Add composite index for SwapRequests
        modelBuilder.Entity<SwapRequest>()
            .HasIndex(s => new { s.CompanyId, s.Status, s.CreatedAt });

        // Multitenancy Phase 1: Update UserNotification index to include CompanyId
        modelBuilder.Entity<UserNotification>()
            .HasIndex(n => new { n.CompanyId, n.UserId, n.CreatedAt });

        // Phase 6: DailyNotificationPreference - one preference per user
        modelBuilder.Entity<DailyNotificationPreference>()
            .HasIndex(p => new { p.CompanyId, p.UserId })
            .IsUnique()
            .HasFilter("IsActive = 1"); // Unique only for active records

        // OnDutyRoleSubscription - unique subscription per user per role type
        modelBuilder.Entity<OnDutyRoleSubscription>()
            .HasIndex(s => new { s.CompanyId, s.UserId, s.OnDutyTypeValue })
            .IsUnique()
            .HasFilter("IsActive = 1");

        // Director role: Configure DirectorCompany mappings
        modelBuilder.Entity<DirectorCompany>()
            .HasIndex(dc => new { dc.UserId, dc.CompanyId })
            .IsUnique()
            .HasFilter("IsDeleted = 0"); // Unique only for active records

        modelBuilder.Entity<DirectorCompany>()
            .HasIndex(dc => dc.CompanyId); // For querying directors of a company

        modelBuilder.Entity<DirectorCompany>()
            .HasIndex(dc => dc.UserId); // For querying companies of a director

        // Configure DirectorCompany relationships as optional to avoid EF10622 warning
        // Since AppUser has a global query filter and DirectorCompany is cross-tenant,
        // we mark navigations as optional to acknowledge they might be filtered
        modelBuilder.Entity<DirectorCompany>()
            .HasOne(dc => dc.User)
            .WithMany()
            .HasForeignKey(dc => dc.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters

        modelBuilder.Entity<DirectorCompany>()
            .HasOne(dc => dc.Company)
            .WithMany()
            .HasForeignKey(dc => dc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters

        modelBuilder.Entity<DirectorCompany>()
            .HasOne(dc => dc.GrantedByUser)
            .WithMany()
            .HasForeignKey(dc => dc.GrantedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters

        // Configure UserJoinRequest
        modelBuilder.Entity<UserJoinRequest>()
            .HasIndex(jr => new { jr.Email, jr.CompanyId, jr.Status });

        modelBuilder.Entity<UserJoinRequest>()
            .HasIndex(jr => new { jr.CompanyId, jr.Status, jr.CreatedAt });

        modelBuilder.Entity<UserJoinRequest>()
            .HasOne(jr => jr.Company)
            .WithMany()
            .HasForeignKey(jr => jr.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserJoinRequest>()
            .HasOne(jr => jr.ReviewedByUser)
            .WithMany()
            .HasForeignKey(jr => jr.ReviewedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserJoinRequest>()
            .HasOne(jr => jr.CreatedUser)
            .WithMany()
            .HasForeignKey(jr => jr.CreatedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserJoinRequest>()
            .HasOne(jr => jr.JobType)
            .WithMany()
            .HasForeignKey(jr => jr.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserJoinRequest>()
            .HasOne(jr => jr.Department)
            .WithMany()
            .HasForeignKey(jr => jr.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure RoleAssignmentAudit
        modelBuilder.Entity<RoleAssignmentAudit>()
            .HasIndex(ra => new { ra.CompanyId, ra.TargetUserId, ra.Timestamp });

        modelBuilder.Entity<RoleAssignmentAudit>()
            .HasIndex(ra => new { ra.ChangedBy, ra.Timestamp });

        // Configure AuditLog
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.CompanyId, a.Timestamp });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.CompanyId, a.UserId, a.Timestamp });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.CompanyId, a.Action });

        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.Company)
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure ProfileChangeAudit
        modelBuilder.Entity<ProfileChangeAudit>()
            .HasIndex(pca => new { pca.CompanyId, pca.TargetUserId, pca.Timestamp });

        modelBuilder.Entity<ProfileChangeAudit>()
            .HasIndex(pca => new { pca.CompanyId, pca.ChangedBy, pca.Timestamp });

        modelBuilder.Entity<ProfileChangeAudit>()
            .HasOne(pca => pca.TargetUser)
            .WithMany()
            .HasForeignKey(pca => pca.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProfileChangeAudit>()
            .HasOne(pca => pca.ChangedByUser)
            .WithMany()
            .HasForeignKey(pca => pca.ChangedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProfileChangeAudit>()
            .HasOne(pca => pca.Company)
            .WithMany()
            .HasForeignKey(pca => pca.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Chore
        modelBuilder.Entity<Chore>()
            .Property(c => c.Date).HasConversion(dateConverter);

        // Composite index for querying chores by company, user, and date
        modelBuilder.Entity<Chore>()
            .HasIndex(c => new { c.CompanyId, c.UserId, c.Date });

        // Index for querying by company and date
        modelBuilder.Entity<Chore>()
            .HasIndex(c => new { c.CompanyId, c.Date });

        // Unique constraint: Only one active chore per user per day
        // Filter ensures canceled chores don't count toward uniqueness
        modelBuilder.Entity<Chore>()
            .HasIndex(c => new { c.CompanyId, c.UserId, c.Date, c.CanceledAt })
            .IsUnique()
            .HasFilter("CanceledAt IS NULL");

        // Configure relationships
        modelBuilder.Entity<Chore>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Chore>()
            .HasOne(c => c.Creator)
            .WithMany()
            .HasForeignKey(c => c.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Chore>()
            .HasOne(c => c.Canceler)
            .WithMany()
            .HasForeignKey(c => c.CanceledBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Chore>()
            .HasOne(c => c.Company)
            .WithMany()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Chore>()
            .HasOne(c => c.Molecule)
            .WithMany()
            .HasForeignKey(c => c.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chore → ChoreType relationship (for calendar categorization)
        modelBuilder.Entity<Chore>()
            .HasOne(c => c.ChoreType)
            .WithMany(ct => ct.Chores)
            .HasForeignKey(c => c.ChoreTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        // Index for molecule-scoped chore queries
        modelBuilder.Entity<Chore>()
            .HasIndex(c => new { c.MoleculeId, c.Date });

        // Configure ChoreType (Excel Calendars feature)
        modelBuilder.Entity<ChoreType>(entity =>
        {
            entity.HasKey(e => e.Id);
            // SECURITY FIX: Use WithMany(m => m.ChoreTypes) to prevent shadow FK MoleculeId1
            entity.HasOne(e => e.Molecule)
                .WithMany(m => m.ChoreTypes)
                .HasForeignKey(e => e.MoleculeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.MoleculeId, e.Name }).IsUnique();
        });

        // Configure ShiftCapacityOverride (Excel Calendars feature)
        modelBuilder.Entity<ShiftCapacityOverride>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Date).HasConversion(dateConverter);
            entity.HasIndex(e => new { e.ShiftTypeId, e.MoleculeId, e.JobTypeId, e.Date }).IsUnique();
            entity.HasOne(e => e.ShiftType).WithMany().HasForeignKey(e => e.ShiftTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Molecule).WithMany().HasForeignKey(e => e.MoleculeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.JobType).WithMany().HasForeignKey(e => e.JobTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // Configure UserDayNote (Excel Calendars feature)
        modelBuilder.Entity<UserDayNote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Date, e.CompanyId }).IsUnique();
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // Configure CalendarTextEntry (unified: Quick Entry text items + Overview notes)
        modelBuilder.Entity<CalendarTextEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Date }); // NOT unique — multiple entries per cell (QuickEntry allows multiples; OverviewNote uniqueness enforced at service level)
            entity.HasIndex(e => e.CompanyId); // Query filter performance
            entity.Property(e => e.Text).HasMaxLength(500);
            entity.Property(e => e.EntryType).HasDefaultValue(CalendarTextEntryType.QuickEntry);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Language Management
        // CompanyLanguageSettings: Unique index on CompanyId (one settings per company)
        modelBuilder.Entity<CompanyLanguageSettings>()
            .HasIndex(cls => cls.CompanyId)
            .IsUnique();

        modelBuilder.Entity<CompanyLanguageSettings>()
            .HasOne(cls => cls.Company)
            .WithMany()
            .HasForeignKey(cls => cls.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompanyLanguageSettings>()
            .HasOne(cls => cls.CreatedByUser)
            .WithMany()
            .HasForeignKey(cls => cls.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompanyLanguageSettings>()
            .HasOne(cls => cls.UpdatedByUser)
            .WithMany()
            .HasForeignKey(cls => cls.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // CompanyLocalizationOverrides: Unique index on (CompanyId, Culture, ResourceKey)
        modelBuilder.Entity<CompanyLocalizationOverride>()
            .HasIndex(clo => new { clo.CompanyId, clo.Culture, clo.ResourceKey })
            .IsUnique();

        // Performance index for loading all overrides for a company/culture
        modelBuilder.Entity<CompanyLocalizationOverride>()
            .HasIndex(clo => new { clo.CompanyId, clo.Culture });

        modelBuilder.Entity<CompanyLocalizationOverride>()
            .HasOne(clo => clo.Company)
            .WithMany()
            .HasForeignKey(clo => clo.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompanyLocalizationOverride>()
            .HasOne(clo => clo.CreatedByUser)
            .WithMany()
            .HasForeignKey(clo => clo.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompanyLocalizationOverride>()
            .HasOne(clo => clo.UpdatedByUser)
            .WithMany()
            .HasForeignKey(clo => clo.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure OnDuty (Global/Public Table - No CompanyId)
        modelBuilder.Entity<OnDuty>()
            .Property(o => o.Date).HasConversion(dateConverter);

        // Index for querying on-duty by user and date
        modelBuilder.Entity<OnDuty>()
            .HasIndex(o => new { o.UserId, o.Date });

        // Index for querying by date (for calendar views)
        modelBuilder.Entity<OnDuty>()
            .HasIndex(o => o.Date);

        // Index for querying by date and type
        modelBuilder.Entity<OnDuty>()
            .HasIndex(o => new { o.Date, o.Type });

        // Unique constraint: Only one active on-duty per user per day per type
        // Filter ensures canceled on-duty assignments don't count toward uniqueness
        modelBuilder.Entity<OnDuty>()
            .HasIndex(o => new { o.UserId, o.Date, o.Type, o.CanceledAt })
            .IsUnique()
            .HasFilter("CanceledAt IS NULL");

        // Configure relationships
        modelBuilder.Entity<OnDuty>()
            .HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters on AppUser

        modelBuilder.Entity<OnDuty>()
            .HasOne(o => o.Creator)
            .WithMany()
            .HasForeignKey(o => o.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters on AppUser

        modelBuilder.Entity<OnDuty>()
            .HasOne(o => o.Canceler)
            .WithMany()
            .HasForeignKey(o => o.CanceledBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters on AppUser

        // Multitenancy Phase 2: Global query filters for automatic tenant scoping
        if (_tenantResolver != null)
        {
            // ShiftType: NO query filter — scope-based visibility (molecule/area), not tenant-based

            modelBuilder.Entity<ShiftInstance>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<ShiftAssignment>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<TimeOffRequest>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<SwapRequest>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<UserNotification>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<AuditLog>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<ProfileChangeAudit>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<Chore>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // SECURITY FIX: Add query filters for previously missing entities
            modelBuilder.Entity<AppUser>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<AppConfig>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<RoleAssignmentAudit>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<UserJoinRequest>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<TeamCalendar>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // DistributionList: standard tenant filter for provenance/interceptor consistency.
            // NOTE: real visibility scope is the molecule — DistributionListService deliberately bypasses
            // this filter with IgnoreQueryFilters() + MoleculeId so lists are shared across all companies
            // in a molecule (same pattern as ShiftCalendarService.GetUsersForCalendarAsync).
            modelBuilder.Entity<DistributionList>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // EmailConfig: custom filter includes BOTH global (CompanyId = null) AND tenant-scoped configs
            modelBuilder.Entity<EmailConfig>()
                .HasQueryFilter(e => e.CompanyId == null || e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // JusticeTarget: same pattern as EmailConfig — Global/Area/Molecule rows have CompanyId = null
            // and must remain visible to every tenant; Company-scoped overrides match the active tenant.
            modelBuilder.Entity<JusticeTarget>()
                .HasQueryFilter(e => e.CompanyId == null || e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<Feedback>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // ✅ PHASE 19: Game scores query filter for tenant scoping
            modelBuilder.Entity<GameScore>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // Language Management: Query filters for tenant scoping
            modelBuilder.Entity<CompanyLanguageSettings>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<CompanyLocalizationOverride>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<EmailApiLog>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // Ops Console Scheduler: Query filters for tenant scoping
            modelBuilder.Entity<ShiftProgram>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<MasterProgram>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // Announcements Feed: Query filter for tenant scoping
            modelBuilder.Entity<Announcement>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // Excel Calendars: Query filter for UserDayNote tenant scoping
            modelBuilder.Entity<UserDayNote>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // Calendar Text Entry: Query filter for tenant scoping
            modelBuilder.Entity<CalendarTextEntry>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // Vacation Approval Rules: Query filter for tenant scoping
            modelBuilder.Entity<VacationApprovalRule>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // SECURITY FIX: Add missing query filters for entities with CompanyId + IBelongsToCompany
            modelBuilder.Entity<GriffinConfig>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<GriffinApiLog>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<EmailTemplateCustomization>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<OnDutyRoleSubscription>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<DailyNotificationPreference>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // SECURITY FIX: Add query filters for API key entities (cross-tenant vulnerability)
            modelBuilder.Entity<ApiKey>()
                .HasQueryFilter(k => k.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<ApiKeyRequest>()
                .HasQueryFilter(r => r.CompanyId == _tenantResolver.GetCurrentTenantId());

            modelBuilder.Entity<CompanySettings>()
                .HasQueryFilter(cs => cs.CompanyId == _tenantResolver.GetCurrentTenantId());

            // Note: ApiRequestLog does NOT have a query filter — CompanyId is nullable (logs unauthenticated attempts)

            // Home Rotation System: Query filters for tenant scoping
            modelBuilder.Entity<HomeType>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());
            modelBuilder.Entity<HomeTypeOverride>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

            // Note: ProgramDay and MasterProgramItem don't need query filters - accessed through parent entities
            // Note: DirectorCompany does NOT have query filter - it's a cross-tenant mapping table
            // Note: OnDuty does NOT have query filter - it's a global/public table visible across all tenancies
        }

        // Configure OnDutyTypeConfig (Global Table - No query filter)
        modelBuilder.Entity<OnDutyTypeConfig>()
            .HasIndex(c => c.TypeValue)
            .IsUnique();

        modelBuilder.Entity<OnDutyTypeConfig>()
            .HasIndex(c => new { c.TypeValue, c.IsActive });

        // Configure EmailConfig
        // Unique constraint: Only one config per company (null CompanyId = global, also unique)
        modelBuilder.Entity<EmailConfig>()
            .HasIndex(ec => ec.CompanyId)
            .IsUnique()
            .HasFilter(null); // SQLite: allows one NULL row for global config

        modelBuilder.Entity<EmailConfig>()
            .HasOne<Company>()
            .WithMany()
            .HasForeignKey(ec => ec.CompanyId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Configure EmailApiLog
        modelBuilder.Entity<EmailApiLog>()
            .HasIndex(e => new { e.CompanyId, e.Timestamp });

        modelBuilder.Entity<EmailApiLog>()
            .HasIndex(e => new { e.CompanyId, e.Success });

        modelBuilder.Entity<EmailApiLog>()
            .HasOne(e => e.Company)
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Feedback
        modelBuilder.Entity<Feedback>()
            .HasIndex(f => new { f.CompanyId, f.CreatedAt });

        modelBuilder.Entity<Feedback>()
            .HasIndex(f => new { f.CompanyId, f.Status, f.CreatedAt });

        modelBuilder.Entity<Feedback>()
            .HasOne(f => f.Submitter)
            .WithMany()
            .HasForeignKey(f => f.SubmittedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Feedback>()
            .HasOne(f => f.StatusUpdater)
            .WithMany()
            .HasForeignKey(f => f.StatusUpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // API Tables Configuration
        modelBuilder.Entity<ApiKey>()
            .HasIndex(k => k.CompanyId);

        modelBuilder.Entity<ApiKey>()
            .HasIndex(k => k.KeyHash).IsUnique();

        modelBuilder.Entity<ApiKey>()
            .HasOne(k => k.Company)
            .WithMany()
            .HasForeignKey(k => k.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApiKey>()
            .HasOne(k => k.CreatedByUser)
            .WithMany()
            .HasForeignKey(k => k.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters

        modelBuilder.Entity<ApiRequestLog>()
            .HasIndex(l => new { l.CompanyId, l.Timestamp });

        modelBuilder.Entity<ApiRequestLog>()
            .HasIndex(l => new { l.ApiKeyId, l.Timestamp });

        modelBuilder.Entity<ApiRequestLog>()
            .HasIndex(l => l.CorrelationId);

        modelBuilder.Entity<ApiRequestLog>()
            .HasOne(l => l.ApiKey)
            .WithMany()
            .HasForeignKey(l => l.ApiKeyId)
            .OnDelete(DeleteBehavior.SetNull);

        // Configure ApiKeyRequest
        modelBuilder.Entity<ApiKeyRequest>()
            .HasIndex(r => new { r.CompanyId, r.Status, r.RequestedAt });

        modelBuilder.Entity<ApiKeyRequest>()
            .HasIndex(r => new { r.RequestedBy, r.Status });

        modelBuilder.Entity<ApiKeyRequest>()
            .HasOne(r => r.Company)
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApiKeyRequest>()
            .HasOne(r => r.RequestedByUser)
            .WithMany()
            .HasForeignKey(r => r.RequestedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Optional due to query filters on AppUser

        modelBuilder.Entity<ApiKeyRequest>()
            .HasOne(r => r.ReviewedByUser)
            .WithMany()
            .HasForeignKey(r => r.ReviewedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<ApiKeyRequest>()
            .HasOne(r => r.GeneratedApiKey)
            .WithMany()
            .HasForeignKey(r => r.GeneratedApiKeyId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // Configure TeamCalendar
        modelBuilder.Entity<TeamCalendar>()
            .HasIndex(tc => new { tc.CompanyId, tc.OwnerId, tc.Name })
            .IsUnique()
            .HasFilter("IsDeleted = 0"); // Unique name per owner when not deleted

        modelBuilder.Entity<TeamCalendar>()
            .HasIndex(tc => new { tc.CompanyId, tc.OwnerId, tc.CreatedAt });

        modelBuilder.Entity<TeamCalendar>()
            .HasOne(tc => tc.Company)
            .WithMany()
            .HasForeignKey(tc => tc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TeamCalendar>()
            .HasOne(tc => tc.Owner)
            .WithMany()
            .HasForeignKey(tc => tc.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure TeamCalendarMember
        modelBuilder.Entity<TeamCalendarMember>()
            .HasIndex(tcm => new { tcm.TeamCalendarId, tcm.MemberUserId })
            .IsUnique(); // One member per calendar

        modelBuilder.Entity<TeamCalendarMember>()
            .HasIndex(tcm => tcm.MemberUserId); // For querying member's calendars

        modelBuilder.Entity<TeamCalendarMember>()
            .HasOne(tcm => tcm.TeamCalendar)
            .WithMany(tc => tc.Members)
            .HasForeignKey(tcm => tcm.TeamCalendarId)
            .OnDelete(DeleteBehavior.Cascade); // Delete members when calendar deleted

        modelBuilder.Entity<TeamCalendarMember>()
            .HasOne(tcm => tcm.Member)
            .WithMany()
            .HasForeignKey(tcm => tcm.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure DistributionList (molecule-scoped, shared user groups for calendar organization)
        modelBuilder.Entity<DistributionList>()
            .Property(dl => dl.Name)
            .UseCollation("NOCASE"); // case-insensitive (ASCII-folding; Hebrew has no case) — makes the unique index case-insensitive

        modelBuilder.Entity<DistributionList>()
            .HasIndex(dl => new { dl.MoleculeId, dl.Name })
            .IsUnique(); // one list name per molecule (case-insensitive via Name's NOCASE collation)

        modelBuilder.Entity<DistributionList>()
            .HasIndex(dl => dl.CompanyId); // provenance lookups

        modelBuilder.Entity<DistributionList>()
            .HasOne(dl => dl.Company)
            .WithMany()
            .HasForeignKey(dl => dl.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DistributionList>()
            .HasOne(dl => dl.Molecule)
            .WithMany()
            .HasForeignKey(dl => dl.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DistributionList>()
            .HasOne(dl => dl.Creator)
            .WithMany()
            .HasForeignKey(dl => dl.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure DistributionListMember (join entity — tenancy scoped via parent list)
        modelBuilder.Entity<DistributionListMember>()
            .HasIndex(dlm => new { dlm.DistributionListId, dlm.UserId })
            .IsUnique(); // one membership per (list, user)

        modelBuilder.Entity<DistributionListMember>()
            .HasIndex(dlm => dlm.UserId); // for querying a user's lists

        modelBuilder.Entity<DistributionListMember>()
            .HasOne(dlm => dlm.DistributionList)
            .WithMany(dl => dl.Members)
            .HasForeignKey(dlm => dlm.DistributionListId)
            .OnDelete(DeleteBehavior.Cascade); // delete members when list deleted

        modelBuilder.Entity<DistributionListMember>()
            .HasOne(dlm => dlm.User)
            .WithMany()
            .HasForeignKey(dlm => dlm.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ✅ PHASE 19: Configure GameScore entity
        modelBuilder.Entity<GameScore>()
            .HasIndex(gs => new { gs.CompanyId, gs.Score })
            .IsDescending(false, true); // CompanyId ascending, Score descending for leaderboard

        modelBuilder.Entity<GameScore>()
            .HasIndex(gs => new { gs.CompanyId, gs.CurrentMonth, gs.Score })
            .IsDescending(false, false, true); // For monthly leaderboard

        modelBuilder.Entity<GameScore>()
            .HasIndex(gs => new { gs.UserId, gs.PlayedAt }); // For user's score history

        modelBuilder.Entity<GameScore>()
            .HasOne(gs => gs.User)
            .WithMany()
            .HasForeignKey(gs => gs.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GameScore>()
            .HasOne(gs => gs.Company)
            .WithMany()
            .HasForeignKey(gs => gs.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Email Template Customization configuration
        modelBuilder.Entity<EmailTemplateCustomization>()
            .HasIndex(e => new { e.CompanyId, e.TemplateType })
            .IsUnique(); // One template per company per type

        modelBuilder.Entity<EmailTemplateCustomization>()
            .HasOne(e => e.Company)
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // ========================================
        // v3.0 Organizational Hierarchy Configurations
        // ========================================

        // Project → Area relationship
        modelBuilder.Entity<Area>()
            .HasOne(a => a.Project)
            .WithMany(p => p.Areas)
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // Area → Molecule relationship
        modelBuilder.Entity<Molecule>()
            .HasOne(m => m.Area)
            .WithMany(a => a.Molecules)
            .HasForeignKey(m => m.AreaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Molecule → Company relationship
        modelBuilder.Entity<Company>()
            .HasOne(c => c.Molecule)
            .WithMany(m => m.Companies)
            .HasForeignKey(c => c.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Nullable during migration period

        // Molecule → Department relationship
        modelBuilder.Entity<Department>()
            .HasOne(d => d.Molecule)
            .WithMany(m => m.Departments)
            .HasForeignKey(d => d.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Area → JobType relationship (JobTypes are Area-scoped)
        modelBuilder.Entity<JobType>()
            .HasOne(jt => jt.Area)
            .WithMany(a => a.JobTypes)
            .HasForeignKey(jt => jt.AreaId)
            .OnDelete(DeleteBehavior.Restrict);

        // JobType → Molecule (optional molecule-specific scoping)
        modelBuilder.Entity<JobType>()
            .HasOne(jt => jt.Molecule)
            .WithMany()
            .HasForeignKey(jt => jt.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<JobType>()
            .HasIndex(jt => jt.MoleculeId);

        // AppUser → JobType relationship
        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.JobType)
            .WithMany(jt => jt.Users)
            .HasForeignKey(u => u.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // AppUser → Department relationship
        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.Department)
            .WithMany(d => d.Users)
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // ========================================
        // ShiftGrouping Configurations
        // ========================================

        // ShiftGrouping → Molecule relationship
        modelBuilder.Entity<ShiftGrouping>()
            .HasOne(sg => sg.Molecule)
            .WithMany(m => m.ShiftGroupings)
            .HasForeignKey(sg => sg.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ShiftGrouping → JobType relationship (for per-Molecule/JobType groupings)
        modelBuilder.Entity<ShiftGrouping>()
            .HasOne(sg => sg.JobType)
            .WithMany()
            .HasForeignKey(sg => sg.JobTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        // Unique index for ShiftGrouping (Molecule, JobType, Name) when JobType is set
        modelBuilder.Entity<ShiftGrouping>()
            .HasIndex(sg => new { sg.MoleculeId, sg.JobTypeId, sg.Name })
            .IsUnique()
            .HasFilter("JobTypeId IS NOT NULL");

        // ShiftGroupingCompany: Composite primary key
        modelBuilder.Entity<ShiftGroupingCompany>()
            .HasKey(sgc => new { sgc.ShiftGroupingId, sgc.CompanyId });

        modelBuilder.Entity<ShiftGroupingCompany>()
            .HasOne(sgc => sgc.ShiftGrouping)
            .WithMany(sg => sg.Companies)
            .HasForeignKey(sgc => sgc.ShiftGroupingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShiftGroupingCompany>()
            .HasOne(sgc => sgc.Company)
            .WithMany()
            .HasForeignKey(sgc => sgc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // ShiftGroupingJobType: Composite primary key
        modelBuilder.Entity<ShiftGroupingJobType>()
            .HasKey(sgjt => new { sgjt.ShiftGroupingId, sgjt.JobTypeId });

        modelBuilder.Entity<ShiftGroupingJobType>()
            .HasOne(sgjt => sgjt.ShiftGrouping)
            .WithMany(sg => sg.JobTypes)
            .HasForeignKey(sgjt => sgjt.ShiftGroupingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShiftGroupingJobType>()
            .HasOne(sgjt => sgjt.JobType)
            .WithMany()
            .HasForeignKey(sgjt => sgjt.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ========================================
        // ShiftCategory Configurations
        // ShiftCategory: NO query filter — molecule-scoped visibility (like ShiftType/ShiftGrouping),
        // not tenant-based. Filtered explicitly by MoleculeId at every call site.
        // ========================================

        modelBuilder.Entity<ShiftCategory>()
            .HasOne(sc => sc.Molecule)
            .WithMany()
            .HasForeignKey(sc => sc.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Lookup index + unique category name within a molecule
        modelBuilder.Entity<ShiftCategory>()
            .HasIndex(sc => sc.MoleculeId);
        modelBuilder.Entity<ShiftCategory>()
            .HasIndex(sc => new { sc.MoleculeId, sc.Name })
            .IsUnique();

        // ShiftType → ShiftCategory: a shift element belongs to at most one category.
        // SetNull so deleting a category un-categorizes its shift types rather than deleting them.
        modelBuilder.Entity<ShiftType>()
            .HasOne(st => st.Category)
            .WithMany(sc => sc.ShiftTypes)
            .HasForeignKey(st => st.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ShiftType>()
            .HasIndex(st => st.CategoryId);

        // UserShiftCategory: many-to-many user ↔ category, unique per pair.
        modelBuilder.Entity<UserShiftCategory>()
            .HasIndex(usc => new { usc.UserId, usc.ShiftCategoryId })
            .IsUnique();

        modelBuilder.Entity<UserShiftCategory>()
            .HasOne(usc => usc.User)
            .WithMany(u => u.ShiftCategories)
            .HasForeignKey(usc => usc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserShiftCategory>()
            .HasOne(usc => usc.ShiftCategory)
            .WithMany(sc => sc.Members)
            .HasForeignKey(usc => usc.ShiftCategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // ========================================
        // Draft Mode Configurations
        // DraftSession/DraftCell: NO query filter — molecule-scoped private sandboxes keyed by OwnerUserId.
        // ========================================

        modelBuilder.Entity<DraftSession>()
            .HasIndex(d => new { d.OwnerUserId, d.MoleculeId, d.Status });

        modelBuilder.Entity<DraftSession>()
            .HasOne(d => d.Owner)
            .WithMany()
            .HasForeignKey(d => d.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DraftCell>()
            .HasOne(c => c.DraftSession)
            .WithMany(d => d.Cells)
            .HasForeignKey(c => c.DraftSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DraftCell>()
            .HasIndex(c => new { c.DraftSessionId, c.ShiftTypeId, c.WorkDate })
            .IsUnique();

        // ========================================
        // Grant System Configurations
        // ========================================

        // GrantType unique key constraint
        modelBuilder.Entity<GrantType>()
            .HasIndex(gt => gt.Key)
            .IsUnique();

        modelBuilder.Entity<GrantType>()
            .HasOne(gt => gt.CreatedByUser)
            .WithMany()
            .HasForeignKey(gt => gt.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Grant relationships
        modelBuilder.Entity<Grant>()
            .HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Grant>()
            .HasOne(g => g.GrantType)
            .WithMany(gt => gt.Grants)
            .HasForeignKey(g => g.GrantTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Grant>()
            .HasOne(g => g.GrantedByUser)
            .WithMany()
            .HasForeignKey(g => g.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<Grant>()
            .HasOne(g => g.Project)
            .WithMany()
            .HasForeignKey(g => g.ProjectId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<Grant>()
            .HasOne(g => g.Area)
            .WithMany()
            .HasForeignKey(g => g.AreaId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<Grant>()
            .HasOne(g => g.Molecule)
            .WithMany()
            .HasForeignKey(g => g.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<Grant>()
            .HasOne(g => g.Department)
            .WithMany()
            .HasForeignKey(g => g.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<Grant>()
            .HasOne(g => g.Company)
            .WithMany()
            .HasForeignKey(g => g.CompanyId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<Grant>()
            .HasOne(g => g.JobType)
            .WithMany()
            .HasForeignKey(g => g.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Grant indexes for query performance
        modelBuilder.Entity<Grant>()
            .HasIndex(g => new { g.UserId, g.GrantTypeId });

        modelBuilder.Entity<Grant>()
            .HasIndex(g => g.GrantTypeId);

        // RoleTemplate unique key constraint
        modelBuilder.Entity<RoleTemplate>()
            .HasIndex(rt => rt.Key)
            .IsUnique();

        // RoleTemplateGrant relationships
        modelBuilder.Entity<RoleTemplateGrant>()
            .HasOne(rtg => rtg.RoleTemplate)
            .WithMany(rt => rt.AutoGrants)
            .HasForeignKey(rtg => rtg.RoleTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoleTemplateGrant>()
            .HasOne(rtg => rtg.GrantType)
            .WithMany(gt => gt.RoleTemplateGrants)
            .HasForeignKey(rtg => rtg.GrantTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // RoleTemplateGrant unique constraint (one grant per role template per JobType)
        // Includes TargetJobTypeId to support dual-JobType grants (e.g., BRDirector ApproveVacations for BR + Hakam)
        modelBuilder.Entity<RoleTemplateGrant>()
            .HasIndex(rtg => new { rtg.RoleTemplateId, rtg.GrantTypeId, rtg.TargetJobTypeId })
            .IsUnique();

        // AppUser → RoleTemplate relationship
        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.RoleTemplate)
            .WithMany()
            .HasForeignKey(u => u.RoleTemplateId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // AppUser → HomeType relationship
        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.HomeType)
            .WithMany()
            .HasForeignKey(u => u.HomeTypeId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // HomeType → Creator relationship (no reverse nav on AppUser)
        modelBuilder.Entity<HomeType>()
            .HasOne(h => h.Creator)
            .WithMany()
            .HasForeignKey(h => h.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // HomeTypeOverride → User/Creator relationships (no reverse nav on AppUser)
        modelBuilder.Entity<HomeTypeOverride>()
            .HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<HomeTypeOverride>()
            .HasOne(o => o.Creator)
            .WithMany()
            .HasForeignKey(o => o.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // HomeTypeOverride → HomeType relationship
        modelBuilder.Entity<HomeTypeOverride>()
            .HasOne(o => o.HomeType)
            .WithMany()
            .HasForeignKey(o => o.HomeTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        // HomeTypeOverride → User (unique per HomeType+User)
        modelBuilder.Entity<HomeTypeOverride>()
            .HasIndex(o => new { o.HomeTypeId, o.UserId })
            .IsUnique();

        // UserJoinRequest → RoleTemplate relationship
        modelBuilder.Entity<UserJoinRequest>()
            .HasOne(jr => jr.RequestedRoleTemplate)
            .WithMany()
            .HasForeignKey(jr => jr.RequestedRoleTemplateId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // RoleTemplateJobTypeLabel relationships
        modelBuilder.Entity<RoleTemplateJobTypeLabel>()
            .HasOne(rtjl => rtjl.RoleTemplate)
            .WithMany(rt => rt.JobTypeLabels)
            .HasForeignKey(rtjl => rtjl.RoleTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoleTemplateJobTypeLabel>()
            .HasOne(rtjl => rtjl.JobType)
            .WithMany()
            .HasForeignKey(rtjl => rtjl.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // RoleTemplateJobTypeLabel composite index for lookup performance
        modelBuilder.Entity<RoleTemplateJobTypeLabel>()
            .HasIndex(rtjl => new { rtjl.RoleTemplateId, rtjl.JobTypeId })
            .IsUnique();

        // RoleAssignmentAudit → RoleTemplate relationships (From/To audit trail)
        modelBuilder.Entity<RoleAssignmentAudit>()
            .HasOne<RoleTemplate>()
            .WithMany()
            .HasForeignKey(ra => ra.FromRoleTemplateId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<RoleAssignmentAudit>()
            .HasOne<RoleTemplate>()
            .WithMany()
            .HasForeignKey(ra => ra.ToRoleTemplateId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // UserRoleAssignment relationships
        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(ura => ura.User)
            .WithMany()
            .HasForeignKey(ura => ura.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(ura => ura.RoleTemplate)
            .WithMany(rt => rt.UserRoles)
            .HasForeignKey(ura => ura.RoleTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(ura => ura.AssignedByUser)
            .WithMany()
            .HasForeignKey(ura => ura.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(ura => ura.Company)
            .WithMany()
            .HasForeignKey(ura => ura.CompanyId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(ura => ura.Department)
            .WithMany()
            .HasForeignKey(ura => ura.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(ura => ura.Molecule)
            .WithMany()
            .HasForeignKey(ura => ura.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(ura => ura.Area)
            .WithMany()
            .HasForeignKey(ura => ura.AreaId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(ura => ura.JobType)
            .WithMany()
            .HasForeignKey(ura => ura.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // UserRoleAssignment indexes
        modelBuilder.Entity<UserRoleAssignment>()
            .HasIndex(ura => new { ura.UserId, ura.RoleTemplateId, ura.IsActive });

        // ========================================
        // Settings Hierarchy Configurations
        // ========================================

        // AreaSettings: One settings per Area
        modelBuilder.Entity<AreaSettings>()
            .HasIndex(asetting => asetting.AreaId)
            .IsUnique();

        modelBuilder.Entity<AreaSettings>()
            .HasOne(asetting => asetting.Area)
            .WithOne(a => a.Settings)
            .HasForeignKey<AreaSettings>(asetting => asetting.AreaId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AreaSettings>()
            .HasOne(asetting => asetting.UpdatedByUser)
            .WithMany()
            .HasForeignKey(asetting => asetting.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // MoleculeSettings: One settings per Molecule
        modelBuilder.Entity<MoleculeSettings>()
            .HasIndex(msetting => msetting.MoleculeId)
            .IsUnique();

        modelBuilder.Entity<MoleculeSettings>()
            .HasOne(msetting => msetting.Molecule)
            .WithOne(m => m.Settings)
            .HasForeignKey<MoleculeSettings>(msetting => msetting.MoleculeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MoleculeSettings>()
            .HasOne(msetting => msetting.UpdatedByUser)
            .WithMany()
            .HasForeignKey(msetting => msetting.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // CompanySettings: One settings per Company
        modelBuilder.Entity<CompanySettings>()
            .HasIndex(csetting => csetting.CompanyId)
            .IsUnique();

        modelBuilder.Entity<CompanySettings>()
            .HasOne(csetting => csetting.Company)
            .WithMany()
            .HasForeignKey(csetting => csetting.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CompanySettings>()
            .HasOne(csetting => csetting.UpdatedByUser)
            .WithMany()
            .HasForeignKey(csetting => csetting.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // ========================================
        // Circle/Friends System Configurations
        // ========================================

        // UserFriendship: Self-referencing relationship
        modelBuilder.Entity<UserFriendship>()
            .HasOne(uf => uf.User)
            .WithMany()
            .HasForeignKey(uf => uf.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserFriendship>()
            .HasOne(uf => uf.Friend)
            .WithMany()
            .HasForeignKey(uf => uf.FriendId)
            .OnDelete(DeleteBehavior.Restrict);  // Prevent cascade loop

        // Unique constraint: One friendship per user pair
        modelBuilder.Entity<UserFriendship>()
            .HasIndex(uf => new { uf.UserId, uf.FriendId })
            .IsUnique();

        // Index for querying user's friends
        modelBuilder.Entity<UserFriendship>()
            .HasIndex(uf => uf.FriendId);

        // ========================================
        // Smart Task System Configurations
        // ========================================

        modelBuilder.Entity<SetupTask>()
            .HasOne(st => st.Molecule)
            .WithMany()
            .HasForeignKey(st => st.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<SetupTask>()
            .HasOne(st => st.Company)
            .WithMany()
            .HasForeignKey(st => st.CompanyId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<SetupTask>()
            .HasOne(st => st.JobType)
            .WithMany()
            .HasForeignKey(st => st.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<SetupTask>()
            .HasOne(st => st.SuggestedUser)
            .WithMany()
            .HasForeignKey(st => st.SuggestedUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<SetupTask>()
            .HasOne(st => st.AssignedToUser)
            .WithMany()
            .HasForeignKey(st => st.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SetupTask>()
            .HasOne(st => st.CompletedByUser)
            .WithMany()
            .HasForeignKey(st => st.CompletedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // SetupTask indexes
        modelBuilder.Entity<SetupTask>()
            .HasIndex(st => new { st.AssignedToUserId, st.Status });

        modelBuilder.Entity<SetupTask>()
            .HasIndex(st => new { st.MoleculeId, st.Status });

        // ========================================
        // Feature Flag Configurations
        // ========================================

        // Unique index: one flag per name/company/user combination
        modelBuilder.Entity<FeatureFlag>()
            .HasIndex(ff => new { ff.Name, ff.CompanyId, ff.UserId })
            .IsUnique();

        // Index for querying by flag name (most common query)
        modelBuilder.Entity<FeatureFlag>()
            .HasIndex(ff => ff.Name);

        // Optional relationship to Company
        modelBuilder.Entity<FeatureFlag>()
            .HasOne(ff => ff.Company)
            .WithMany()
            .HasForeignKey(ff => ff.CompanyId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // Optional relationship to User
        modelBuilder.Entity<FeatureFlag>()
            .HasOne(ff => ff.User)
            .WithMany()
            .HasForeignKey(ff => ff.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // ========================================
        // Client Telemetry Configurations (B-019, B-020, B-021)
        // Local observability for air-gapped environments
        // Note: No tenant scoping - telemetry is system-wide
        // ========================================

        // ClientAnalyticsEvent indexes
        modelBuilder.Entity<ClientAnalyticsEvent>()
            .HasIndex(e => e.Timestamp);

        modelBuilder.Entity<ClientAnalyticsEvent>()
            .HasIndex(e => new { e.EventType, e.Timestamp });

        modelBuilder.Entity<ClientAnalyticsEvent>()
            .HasIndex(e => e.UserIdHash);

        modelBuilder.Entity<ClientAnalyticsEvent>()
            .HasIndex(e => e.SessionId);

        // ClientError indexes
        modelBuilder.Entity<ClientError>()
            .HasIndex(e => e.Timestamp);

        modelBuilder.Entity<ClientError>()
            .HasIndex(e => new { e.ErrorType, e.Timestamp });

        modelBuilder.Entity<ClientError>()
            .HasIndex(e => e.UserIdHash);

        modelBuilder.Entity<ClientError>()
            .HasIndex(e => e.PageUrl);

        // PerformanceMetric indexes
        modelBuilder.Entity<PerformanceMetric>()
            .HasIndex(m => m.Timestamp);

        modelBuilder.Entity<PerformanceMetric>()
            .HasIndex(m => new { m.MetricName, m.Timestamp });

        modelBuilder.Entity<PerformanceMetric>()
            .HasIndex(m => new { m.MetricName, m.PageUrl });

        modelBuilder.Entity<PerformanceMetric>()
            .HasIndex(m => new { m.MetricName, m.Rating });

        // ========================================
        // Duty Rotation System Configurations
        // ========================================

        // DutyRotation: DateOnly conversion for LastAssignedDate
        modelBuilder.Entity<DutyRotation>()
            .Property(r => r.LastAssignedDate).HasConversion(dateConverter);

        // DutyRotation: tenant query filter
        if (_tenantResolver != null)
        {
            modelBuilder.Entity<DutyRotation>()
                .HasQueryFilter(r => r.CompanyId == _tenantResolver.GetCurrentTenantId());
        }

        // DutyRotation → Company relationship
        modelBuilder.Entity<DutyRotation>()
            .HasOne(r => r.Company)
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // DutyRotation → Creator relationship
        modelBuilder.Entity<DutyRotation>()
            .HasOne(r => r.Creator)
            .WithMany()
            .HasForeignKey(r => r.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters on AppUser

        // DutyRotationEntry → DutyRotation relationship
        modelBuilder.Entity<DutyRotationEntry>()
            .HasOne(e => e.DutyRotation)
            .WithMany(r => r.Entries)
            .HasForeignKey(e => e.DutyRotationId)
            .OnDelete(DeleteBehavior.Cascade);

        // DutyRotationEntry → User relationship
        modelBuilder.Entity<DutyRotationEntry>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);  // Navigation is optional due to query filters on AppUser

        // DutyRotationEntry index for queue ordering
        modelBuilder.Entity<DutyRotationEntry>()
            .HasIndex(e => new { e.DutyRotationId, e.Position });

        // DutyRotationLog: DateOnly conversion for AssignmentDate
        modelBuilder.Entity<DutyRotationLog>()
            .Property(l => l.AssignmentDate).HasConversion(dateConverter);

        // DutyRotationLog → DutyRotation relationship
        modelBuilder.Entity<DutyRotationLog>()
            .HasOne(l => l.DutyRotation)
            .WithMany(r => r.Logs)
            .HasForeignKey(l => l.DutyRotationId)
            .OnDelete(DeleteBehavior.Cascade);

        // DutyRotationLog → AssignedUser relationship
        modelBuilder.Entity<DutyRotationLog>()
            .HasOne(l => l.AssignedUser)
            .WithMany()
            .HasForeignKey(l => l.AssignedUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // DutyRotationLog → SkippedUser relationship
        modelBuilder.Entity<DutyRotationLog>()
            .HasOne(l => l.SkippedUser)
            .WithMany()
            .HasForeignKey(l => l.SkippedUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // DutyRotationLog → OnDuty relationship
        modelBuilder.Entity<DutyRotationLog>()
            .HasOne(l => l.OnDuty)
            .WithMany()
            .HasForeignKey(l => l.OnDutyId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // DutyRotationLog indexes
        modelBuilder.Entity<DutyRotationLog>()
            .HasIndex(l => new { l.DutyRotationId, l.AssignmentDate });

        // ========================================
        // Vacation Approval Rule Configurations
        // ========================================

        // VacationApprovalRule → Company relationship
        modelBuilder.Entity<VacationApprovalRule>()
            .HasOne(r => r.Company)
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // VacationApprovalRule → JobType relationship
        modelBuilder.Entity<VacationApprovalRule>()
            .HasOne(r => r.JobType)
            .WithMany()
            .HasForeignKey(r => r.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // VacationApprovalRule → ApproverUser relationship
        modelBuilder.Entity<VacationApprovalRule>()
            .HasOne(r => r.ApproverUser)
            .WithMany()
            .HasForeignKey(r => r.ApproverUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Composite index for querying rules by company, job type, and priority
        modelBuilder.Entity<VacationApprovalRule>()
            .HasIndex(r => new { r.CompanyId, r.JobTypeId, r.Priority });

        // ========================================
        // Store Hours & Quick Info Widget
        // ========================================

        // Store relationships (area-scoped, NO query filter — global table like OnDutyTypeConfig)
        modelBuilder.Entity<Store>(entity =>
        {
            entity.HasOne(s => s.Area)
                .WithMany()
                .HasForeignKey(s => s.AreaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StoreHoursEntry relationships + TimeOnly converters (reuses existing timeConverter)
        modelBuilder.Entity<StoreHoursEntry>(entity =>
        {
            entity.HasOne(h => h.Store)
                .WithMany(s => s.StoreHoursEntries)
                .HasForeignKey(h => h.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(h => h.OpenTime).HasConversion(timeConverter);
            entity.Property(h => h.CloseTime).HasConversion(timeConverter);
        });

        // QuickInfoConfig relationships (molecule-scoped, NO query filter — service scopes by moleculeId)
        modelBuilder.Entity<QuickInfoConfig>(entity =>
        {
            entity.HasOne(q => q.Molecule)
                .WithMany()
                .HasForeignKey(q => q.MoleculeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(q => new { q.MoleculeId, q.IsEnabled });
        });

        // AreaCalendarPalette (2026-04-15): unique per Area, cascade on Area deletion.
        modelBuilder.Entity<AreaCalendarPalette>(b =>
        {
            b.HasIndex(p => p.AreaId).IsUnique();
            b.HasOne(p => p.Area)
                .WithMany()
                .HasForeignKey(p => p.AreaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.Property(p => p.ShiftMorning).HasMaxLength(7);
            b.Property(p => p.ShiftAfternoon).HasMaxLength(7);
            b.Property(p => p.ShiftNight).HasMaxLength(7);
            b.Property(p => p.ShiftHome).HasMaxLength(7);
            b.Property(p => p.OnDuty).HasMaxLength(7);
            b.Property(p => p.Chore).HasMaxLength(7);
            b.Property(p => p.Vacation).HasMaxLength(7);
        });

        // ============================================
        // Justice Analytics (2026-05-03)
        // ============================================
        // JusticeTarget: configurable expected workload per (WorkType, ScopeKind, ScopeId).
        // - Unique on the triple so settings upserts can rely on it.
        // - CompanyId is intentionally optional; the query filter (added above when tenantResolver
        //   is available) handles the "global row visible to every tenant" case.
        // - decimal(7,2) gives us up to 99,999.99 expected items per period (more than enough).
        modelBuilder.Entity<JusticeTarget>(b =>
        {
            b.HasIndex(t => new { t.WorkType, t.ScopeKind, t.ScopeId }).IsUnique();
            b.HasIndex(t => new { t.ScopeKind, t.ScopeId });
            b.Property(t => t.ExpectedCount).HasPrecision(7, 2);
            b.Property(t => t.Note).HasMaxLength(200);
        });

        // Justice indexes on existing tables — added 2026-05-03 to support cross-molecule
        // / cross-area workload aggregation queries.
        // OnDuty is a global table (no CompanyId, no query filter); these indexes support the
        // per-user and date-range aggregations the Justice Service runs.
        modelBuilder.Entity<OnDuty>()
            .HasIndex(od => new { od.Date, od.UserId });
        modelBuilder.Entity<OnDuty>()
            .HasIndex(od => new { od.Date, od.CanceledAt });
        // ShiftInstance has no MoleculeId column (molecule scoping happens via ShiftType.MoleculeId
        // join or via a list of CompanyIds in the molecule). The existing (CompanyId, WorkDate) index
        // already covers the dominant query path. No new ShiftInstance index needed for Phase 1.

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Safe DateOnly parser for EF value converter. Returns DateOnly.MinValue on invalid input
    /// instead of throwing FormatException (which would crash on corrupted DB data).
    /// </summary>
    private static DateOnly SafeParseDateOnly(string value)
    {
        return DateOnly.TryParse(value, out var d) ? d : DateOnly.MinValue;
    }

    /// <summary>
    /// Safe TimeOnly parser for EF value converter. Returns TimeOnly.MinValue on invalid input
    /// instead of throwing FormatException (which would crash on corrupted DB data).
    /// </summary>
    private static TimeOnly SafeParseTimeOnly(string value)
    {
        return TimeOnly.TryParse(value, out var t) ? t : TimeOnly.MinValue;
    }
}
