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
    public DbSet<TeamCalendar> TeamCalendars => Set<TeamCalendar>();
    public DbSet<TeamCalendarMember> TeamCalendarMembers => Set<TeamCalendarMember>();
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();
    public DbSet<EmailApiLog> EmailApiLogs => Set<EmailApiLog>();
    public DbSet<EmailTemplateCustomization> EmailTemplateCustomizations => Set<EmailTemplateCustomization>();
    public DbSet<GriffinConfig> GriffinConfigs => Set<GriffinConfig>();
    public DbSet<GriffinApiLog> GriffinApiLogs => Set<GriffinApiLog>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();

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

    // API Sidecar Tables (no query filters - not tenant-scoped in traditional sense)
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
    public DbSet<Molecule> Molecules => Set<Molecule>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobType> JobTypes => Set<JobType>();

    // Shift Groupings (Company + JobType combinations for shift scheduling)
    public DbSet<ShiftGrouping> ShiftGroupings => Set<ShiftGrouping>();
    public DbSet<ShiftGroupingCompany> ShiftGroupingCompanies => Set<ShiftGroupingCompany>();
    public DbSet<ShiftGroupingJobType> ShiftGroupingJobTypes => Set<ShiftGroupingJobType>();

    // Grant System (107 built-in grants, 11 role templates)
    public DbSet<GrantType> GrantTypes => Set<GrantType>();
    public DbSet<Grant> Grants => Set<Grant>();
    public DbSet<RoleTemplate> RoleTemplates => Set<RoleTemplate>();
    public DbSet<RoleTemplateGrant> RoleTemplateGrants => Set<RoleTemplateGrant>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

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
    // Announcements Feed (tenant-scoped)
    // ========================================
    public DbSet<Announcement> Announcements => Set<Announcement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dateConverter = new ValueConverter<DateOnly, string>(
            v => v.ToString("yyyy-MM-dd"),
            v => DateOnly.Parse(v));

        var timeConverter = new ValueConverter<TimeOnly, string>(
            v => v.ToString("HH:mm"),
            v => TimeOnly.Parse(v));

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

        // Multitenancy: Add composite index for ShiftType (CompanyId, Key)
        modelBuilder.Entity<ShiftType>()
            .HasIndex(s => new { s.CompanyId, s.Key });

        // Multitenancy Phase 1: Update ShiftAssignment index to include CompanyId
        modelBuilder.Entity<ShiftAssignment>()
            .HasIndex(a => new { a.CompanyId, a.ShiftInstanceId, a.UserId }).IsUnique();

        // Multitenancy Phase 1: Add composite index for TimeOffRequests
        modelBuilder.Entity<TimeOffRequest>()
            .HasIndex(t => new { t.CompanyId, t.UserId, t.StartDate });

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
            .HasFilter("[IsActive] = 1"); // Unique only for active records

        // OnDutyRoleSubscription - unique subscription per user per role type
        modelBuilder.Entity<OnDutyRoleSubscription>()
            .HasIndex(s => new { s.CompanyId, s.UserId, s.OnDutyTypeValue })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        // Director role: Configure DirectorCompany mappings
        modelBuilder.Entity<DirectorCompany>()
            .HasIndex(dc => new { dc.UserId, dc.CompanyId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0"); // Unique only for active records

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
            .OnDelete(DeleteBehavior.SetNull);

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
            .HasFilter("[CanceledAt] IS NULL");

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

        // Index for molecule-scoped chore queries
        modelBuilder.Entity<Chore>()
            .HasIndex(c => new { c.MoleculeId, c.Date });

        // Configure ChoreType (Excel Calendars feature)
        modelBuilder.Entity<ChoreType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Molecule)
                .WithMany()
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
            entity.HasIndex(e => new { e.ShiftTypeId, e.MoleculeId, e.JobTypeId, e.Date }).IsUnique();
            entity.HasOne(e => e.ShiftType).WithMany().HasForeignKey(e => e.ShiftTypeId).OnDelete(DeleteBehavior.Cascade);
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
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
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
            .HasFilter("[CanceledAt] IS NULL");

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
            modelBuilder.Entity<ShiftType>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

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

            modelBuilder.Entity<EmailConfig>()
                .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

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
        // Unique constraint: Only one email configuration per company
        modelBuilder.Entity<EmailConfig>()
            .HasIndex(ec => ec.CompanyId)
            .IsUnique();

        modelBuilder.Entity<EmailConfig>()
            .HasOne<Company>()
            .WithMany()
            .HasForeignKey(ec => ec.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

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
            .HasFilter("[IsDeleted] = 0"); // Unique name per owner when not deleted

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

        // RoleTemplateGrant unique constraint (one grant per role template)
        modelBuilder.Entity<RoleTemplateGrant>()
            .HasIndex(rtg => new { rtg.RoleTemplateId, rtg.GrantTypeId })
            .IsUnique();

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

        base.OnModelCreating(modelBuilder);
    }
}
