using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Data;

/// <summary>
/// EF Core SaveChanges interceptor that automatically sets CompanyId
/// on entities implementing IBelongsToCompany when they're being added.
/// Multitenancy Phase 2: Automatic tenant scoping for new entities with feature flag support
/// </summary>
public class CompanyIdInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompanyIdInterceptor> _logger;

    public CompanyIdInterceptor(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<CompanyIdInterceptor> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        SetCompanyId(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetCompanyId(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SetCompanyId(DbContext? context)
    {
        if (context == null)
            return;

        // Check feature flag for enforcement mode
        var enforceCompanyScope = _configuration.GetValue<bool>("Features:EnforceCompanyScope", false);

        // Resolve ITenantResolver from the current scope
        using var scope = _serviceProvider.CreateScope();
        var tenantResolver = scope.ServiceProvider.GetService<ITenantResolver>();

        if (tenantResolver == null)
        {
            if (enforceCompanyScope)
            {
                _logger.LogWarning(
                    "CompanyId interceptor: ITenantResolver not available but EnforceCompanyScope is enabled. " +
                    "Entity may not be properly company-scoped.");
            }
            else
            {
                _logger.LogWarning(
                    "CompanyId interceptor: ITenantResolver not available. " +
                    "EnforceCompanyScope is disabled, allowing entities to be saved without CompanyId validation.");
            }
            return;
        }

        // Check if we have a tenant context
        var hasTenant = tenantResolver.HasTenant();
        var companyId = hasTenant ? tenantResolver.GetCurrentTenantId() : 0;

        // Find all entities implementing IBelongsToCompany that are being added
        var entries = context.ChangeTracker
            .Entries<IBelongsToCompany>()
            .Where(e => e.State == EntityState.Added)
            .ToList();

        foreach (var entry in entries)
        {
            var entityType = entry.Entity.GetType().Name;

            // Only set CompanyId if it hasn't been explicitly set (is 0)
            if (entry.Entity.CompanyId == 0)
            {
                if (!hasTenant || companyId == 0)
                {
                    if (enforceCompanyScope)
                    {
                        // Hard block: Prevent orphaned entities that would be invisible to all tenant queries
                        throw new InvalidOperationException(
                            $"CompanyId interceptor: Entity {entityType} cannot be saved — CompanyId cannot be determined " +
                            "and EnforceCompanyScope is enabled. This would create an orphaned record invisible to all tenant-scoped queries.");
                    }
                    else
                    {
                        // Warn mode: Log when data lacks CompanyId
                        _logger.LogWarning(
                            "CompanyId interceptor: Entity {EntityType} (Id: {EntityId}) is being saved with CompanyId=0. " +
                            "Tenant context not available. EnforceCompanyScope is disabled, allowing this operation.",
                            entityType,
                            entry.Entity.GetType().GetProperty("Id")?.GetValue(entry.Entity) ?? "N/A");
                    }
                }
                else
                {
                    // Set CompanyId from tenant resolver
                    entry.Entity.CompanyId = companyId;

                    if (!enforceCompanyScope)
                    {
                        _logger.LogInformation(
                            "CompanyId interceptor: Auto-set CompanyId={CompanyId} for {EntityType}. " +
                            "(Warn mode: EnforceCompanyScope disabled)",
                            companyId,
                            entityType);
                    }
                }
            }
        }
    }
}