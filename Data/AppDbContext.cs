using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ShiftManager.Models;
using ShiftManager.Models.Api;
using ShiftManager.Models.Support;
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

        base.OnModelCreating(modelBuilder);
    }
}
