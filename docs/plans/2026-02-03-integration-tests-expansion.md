# Integration Tests Expansion Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Expand test coverage for critical authorization logic (GrantService scope inheritance), multitenancy isolation, and API endpoints. Target: increase from 11 test files to comprehensive coverage of core services.

**Architecture:** Follow existing test patterns in ShiftManager.Tests. Use xUnit with in-memory SQLite for unit tests, WebApplicationFactory for integration tests. Focus on GrantService edge cases and cross-tenant isolation.

**Tech Stack:** xUnit, Moq, Microsoft.EntityFrameworkCore.InMemory, WebApplicationFactory, FluentAssertions

---

## Current State

Existing test files:
- `UnitTests/Services/DirectorServiceTests.cs`
- `UnitTests/Services/ConcurrencyServiceTests.cs`
- `UnitTests/Services/V3Hierarchy/FriendshipServiceTests.cs`
- `UnitTests/Services/V3Hierarchy/GrantServiceTests.cs`
- `UnitTests/Services/V3Hierarchy/HierarchySettingsServiceTests.cs`
- `UnitTests/Services/V3Hierarchy/ScopeFilterServiceTests.cs`
- `UnitTests/Services/V3Hierarchy/ShiftAssignmentServiceTests.cs`
- `UnitTests/Calendar/CalendarDateValidationTests.cs`
- `IntegrationTests/DirectorCreationTests.cs`
- `IntegrationTests/DirectorCrossTenantTests.cs`
- `IntegrationTests/ProgramManagementAuthorizationTests.cs`

**Gap analysis:** GrantService has basic tests but lacks edge case coverage for scope inheritance, permission cascading, and denial scenarios.

---

## Task 1: Expand GrantService Unit Tests

**Files:**
- Modify: `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs`

**Step 1: Read existing GrantService tests**

Review current test coverage to identify gaps.

**Step 2: Add scope inheritance tests**

```csharp
[Fact]
public async Task HasGrant_InheritsFromParentScope_WhenGrantAtProjectLevel()
{
    // Arrange: User has grant at Project level
    // Act: Check grant at Department level (child of Project)
    // Assert: Grant should be inherited
}

[Fact]
public async Task HasGrant_DoesNotInheritUpward_WhenGrantAtDepartmentLevel()
{
    // Arrange: User has grant at Department level only
    // Act: Check grant at Project level (parent)
    // Assert: Grant should NOT be inherited upward
}

[Fact]
public async Task HasGrant_RespectsExplicitDenial_EvenWithParentGrant()
{
    // Arrange: User has grant at Project level, explicit denial at Department
    // Act: Check grant at denied Department
    // Assert: Denial takes precedence
}
```

**Step 3: Add multi-level hierarchy tests**

```csharp
[Fact]
public async Task HasGrant_TraversesFullHierarchy_ProjectToArea_AreaToMolecule_MoleculeToDepartment()
{
    // Test grant inheritance through all 4 levels
}

[Fact]
public async Task GetEffectiveGrants_ReturnsAllInheritedGrants_ForUserAtLeafNode()
{
    // User at Department level should see grants from Project, Area, Molecule
}
```

**Step 4: Add edge case tests**

```csharp
[Fact]
public async Task HasGrant_HandlesCircularReferences_Gracefully()
{
    // Edge case: malformed hierarchy with cycles
}

[Fact]
public async Task HasGrant_HandlesOrphanedEntities_WithoutParent()
{
    // Department not linked to any Molecule
}

[Fact]
public async Task HasGrant_PerformsWithin100ms_ForDeepHierarchy()
{
    // Performance test: 10-level deep hierarchy
}
```

**Step 5: Run tests**

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~GrantServiceTests" -v normal
```

**Step 6: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs
git commit -m "test: expand GrantService tests for scope inheritance edge cases"
```

---

## Task 2: Add Multitenancy Isolation Tests

**Files:**
- Create: `ShiftManager.Tests/IntegrationTests/MultitenancyIsolationTests.cs`

**Step 1: Create test fixture**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Data;
using ShiftManager.Models;
using Xunit;

namespace ShiftManager.Tests.IntegrationTests;

public class MultitenancyIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MultitenancyIsolationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace DbContext with in-memory database
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase("MultitenancyTests");
                });
            });
        });
    }

    [Fact]
    public async Task QueryFilter_PreventsCrossTenantDataAccess()
    {
        // Arrange: Create data for Company A and Company B
        // Act: Query as Company A user
        // Assert: Only Company A data returned
    }

    [Fact]
    public async Task CompanyIdInterceptor_AutomaticallySetsTenantId_OnNewEntities()
    {
        // Arrange: Set tenant context to Company A
        // Act: Create new ShiftInstance without explicit CompanyId
        // Assert: CompanyId is automatically set to Company A
    }

    [Fact]
    public async Task DirectDbAccess_StillRespectsTenantFilter()
    {
        // Test that even raw queries respect query filters
    }
}
```

**Step 2: Add cross-tenant attack prevention tests**

```csharp
[Fact]
public async Task UpdateEntity_RejectsEntityFromDifferentTenant()
{
    // Arrange: Entity belongs to Company B
    // Act: Try to update as Company A user
    // Assert: Operation fails or data unchanged
}

[Fact]
public async Task DeleteEntity_RejectsEntityFromDifferentTenant()
{
    // Similar to update test
}

[Fact]
public async Task ApiEndpoint_ReturnsOnlyTenantData()
{
    // Test API endpoints respect tenant boundaries
}
```

**Step 3: Commit**

```bash
git add ShiftManager.Tests/IntegrationTests/MultitenancyIsolationTests.cs
git commit -m "test: add multitenancy isolation integration tests"
```

---

## Task 3: Add API Endpoint Tests

**Files:**
- Create: `ShiftManager.Tests/IntegrationTests/ApiEndpointTests.cs`

**Step 1: Create API test class**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ShiftManager.Tests.IntegrationTests;

public class ApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/Api/Calendar/GetMonth")]
    [InlineData("/Api/SessionStatus")]
    public async Task InternalApiEndpoint_RequiresAuthentication(string endpoint)
    {
        // Act: Call endpoint without authentication
        var response = await _client.GetAsync(endpoint);

        // Assert: Should redirect to login or return 401
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Redirect);
    }

    [Theory]
    [InlineData("/api/v1/shifts")]
    [InlineData("/api/v1/employees")]
    public async Task ExternalApiEndpoint_RequiresApiKey(string endpoint)
    {
        // Act: Call without X-API-Key header
        var response = await _client.GetAsync(endpoint);

        // Assert: Should return 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExternalApiEndpoint_AcceptsValidApiKey()
    {
        // Arrange: Add valid API key header
        _client.DefaultRequestHeaders.Add("X-API-Key", "test-valid-key");

        // Act
        var response = await _client.GetAsync("/api/v1/health");

        // Assert: Should succeed
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

**Step 2: Add authorization tests**

```csharp
[Fact]
public async Task AdminEndpoint_RejectsNonAdminUser()
{
    // Test role-based authorization
}

[Fact]
public async Task GrantProtectedEndpoint_RejectsUserWithoutGrant()
{
    // Test grant-based authorization
}
```

**Step 3: Commit**

```bash
git add ShiftManager.Tests/IntegrationTests/ApiEndpointTests.cs
git commit -m "test: add API endpoint authentication and authorization tests"
```

---

## Task 4: Add NotificationService Tests

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/NotificationServiceTests.cs`

**Step 1: Create test class**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

public class NotificationServiceTests
{
    private readonly Mock<IMailService> _mailServiceMock;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<ILogger<NotificationService>> _loggerMock;
    private readonly ApplicationDbContext _db;

    public NotificationServiceTests()
    {
        _mailServiceMock = new Mock<IMailService>();
        _tenantResolverMock = new Mock<ITenantResolver>();
        _loggerMock = new Mock<ILogger<NotificationService>>();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateShiftAddedNotificationAsync_CreatesNotificationForEmployee()
    {
        // Arrange
        var userId = 1;
        var shiftName = "Morning Shift";
        var date = DateTime.Today;

        var service = CreateService();

        // Act
        await service.CreateShiftAddedNotificationAsync(userId, shiftName, date);

        // Assert
        var notification = await _db.UserNotifications.FirstOrDefaultAsync();
        Assert.NotNull(notification);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal(NotificationType.ShiftAssigned, notification.Type);
    }

    [Fact]
    public async Task SendDailyDigestAsync_SendsEmailToEligibleUsers()
    {
        // Arrange: Users with daily digest enabled
        // Act: Send daily digest
        // Assert: Mail service called for each eligible user
    }

    [Fact]
    public async Task SendDayBeforeRemindersAsync_OnlySendsToUsersWithUpcomingShifts()
    {
        // Test day-before reminder filtering
    }

    private NotificationService CreateService()
    {
        var config = new ConfigurationBuilder().Build();
        var localizer = new Mock<IStringLocalizer<SharedResources>>();

        return new NotificationService(
            _db,
            _mailServiceMock.Object,
            _tenantResolverMock.Object,
            config,
            localizer.Object,
            _loggerMock.Object);
    }
}
```

**Step 2: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/NotificationServiceTests.cs
git commit -m "test: add NotificationService unit tests"
```

---

## Task 5: Add AnalyticsService Tests

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/AnalyticsServiceTests.cs`

**Step 1: Create test class**

```csharp
namespace ShiftManager.Tests.UnitTests.Services;

public class AnalyticsServiceTests
{
    [Fact]
    public async Task GetEmployeeHoursAsync_CalculatesCorrectTotalHours()
    {
        // Test hour calculation accuracy
    }

    [Fact]
    public async Task GetUnderstaffingReportAsync_IdentifiesShiftsBelowRequired()
    {
        // Test understaffing detection
    }

    [Fact]
    public async Task GetSwapStatsAsync_CalculatesCorrectApprovalRate()
    {
        // Test swap statistics
    }

    [Fact]
    public async Task CachesResults_ForConfiguredDuration()
    {
        // Test caching behavior
    }
}
```

**Step 2: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/AnalyticsServiceTests.cs
git commit -m "test: add AnalyticsService unit tests"
```

---

## Task 6: Run Full Test Suite and Generate Coverage Report

**Step 1: Run all tests**

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -v normal
```

**Step 2: Generate coverage report (optional)**

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --collect:"XPlat Code Coverage"
```

**Step 3: Verify all tests pass**

Expected: 100% pass rate

**Step 4: Final commit**

```bash
git add -A
git commit -m "test: complete integration tests expansion - all tests passing"
```

---

## Verification Checklist

- [ ] GrantService scope inheritance tests added (5+ new tests)
- [ ] Multitenancy isolation tests added (5+ new tests)
- [ ] API endpoint tests added (5+ new tests)
- [ ] NotificationService tests added (3+ new tests)
- [ ] AnalyticsService tests added (4+ new tests)
- [ ] All tests pass
- [ ] No flaky tests
- [ ] Test execution time < 60 seconds

---

## Estimated Effort: 16-24 hours

