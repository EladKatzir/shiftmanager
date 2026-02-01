# ShiftManager Testing Strategy

**Generated:** 2026-01-12
**Agent:** Antigravity Workspace Intake

---

## Current Test Coverage Assessment

### Existing Tests
**Location:** `ShiftManager.Tests/`
**Structure:**
```
ShiftManager.Tests/
├── IntegrationTests/        # 3 files (content unknown)
├── UnitTests/
│   └── Services/
│       └── DirectorServiceTests.cs  # 10,263 bytes
├── ShiftManager.Tests.csproj
├── bin/
└── obj/
```

**Evidence:** Directory listing `ShiftManager.Tests/`

### Test Project Configuration
**File:** `ShiftManager.Tests/ShiftManager.Tests.csproj` (1,070 bytes)

**Dependencies (from `docs/context.md:51-56`):**
- xUnit 2.5.3 — Test runner
- FluentAssertions 8.7.1 — Assertion library
- Moq 4.20.72 — Mocking framework
- Microsoft.AspNetCore.Mvc.Testing 8.0.10 — Integration testing
- Microsoft.EntityFrameworkCore.InMemory 9.0.9 — In-memory database
- coverlet.collector 6.0.0 — Code coverage

### Current Test Count
**Evidence:** `docs/context.md:29` — "Test Coverage: 15 unit tests (DirectorService only - 100% passing)"

| Test Suite | Count | Coverage |
|------------|-------|----------|
| DirectorServiceTests | 15 | DirectorService 100% |
| Other unit tests | 0 | — |
| Integration tests | Unknown | — |

---

## Test Run Commands

### Run All Tests
```powershell
cd c:\Users\katzi\Downloads\ShiftManager
dotnet test
```

### Run with Detailed Output
```powershell
dotnet test --logger "console;verbosity=detailed"
```

### Run with Coverage
```powershell
dotnet test --collect:"XPlat Code Coverage"
```

**Expected Output:**
```
Test run for C:\...\ShiftManager.Tests.dll (.NETCoreApp,Version=v8.0)
Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15
```

---

## Testing Strategy by Layer

### 1. Unit Tests (Service Layer)

**Target:** Services in `Services/` directory (74 files)

**Pattern:**
```csharp
public class ServiceTests
{
    private readonly Mock<AppDbContext> _dbContextMock;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly ServiceUnderTest _sut;
    
    [Fact]
    public async Task MethodName_Scenario_ExpectedResult()
    {
        // Arrange
        _tenantResolverMock.Setup(x => x.GetCurrentTenantId()).Returns(1);
        
        // Act
        var result = await _sut.MethodAsync();
        
        // Assert
        result.Should().NotBeNull();
    }
}
```

**Priority Services for Testing:**

| Priority | Service | Reason | Risk |
|----------|---------|--------|------|
| 🔴 Critical | `DirectorService` | Multi-tenant access control | ✅ Tested |
| 🔴 Critical | `TraineeService` | Shadowing validation | ❌ Untested |
| 🔴 Critical | `ApiKeyService` | Security-critical | ❌ Untested |
| 🟡 High | `ChoreService` | Complex conflict detection | ❌ Untested |
| 🟡 High | `OnDutyService` | Global table queries | ❌ Untested |
| 🟡 High | `NotificationService` | Many notification types | ❌ Untested |
| 🟡 High | `TenantResolver` | Multi-tenancy foundation | ❌ Untested |
| 🟢 Medium | `AnalyticsService` | Complex aggregations | ❌ Untested |
| 🟢 Medium | `MailService` | External integration | ❌ Untested |
| 🟢 Medium | `TeamCalendarService` | Event aggregation | ❌ Untested |

### 2. Integration Tests

**Target:** End-to-end request flows

**Pattern with WebApplicationFactory:**
```csharp
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    
    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace SQLite with InMemory
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(opt =>
                    opt.UseInMemoryDatabase("TestDb"));
            });
        }).CreateClient();
    }
    
    [Fact]
    public async Task GetUsers_WithValidApiKey_ReturnsUsers()
    {
        // Arrange
        _client.DefaultRequestHeaders.Add("X-API-Key", "test-key");
        
        // Act
        var response = await _client.GetAsync("/api/v1/users");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

### 3. Page Model Tests

**Target:** Razor page OnGet/OnPost handlers

**Pattern:**
```csharp
public class LoginPageTests
{
    [Fact]
    public async Task OnPostAsync_ValidCredentials_RedirectsToCalendar()
    {
        // Arrange
        var pageModel = CreatePageModel();
        pageModel.Email = "admin@local";
        pageModel.Password = "admin123";
        
        // Act
        var result = await pageModel.OnPostAsync();
        
        // Assert
        result.Should().BeOfType<RedirectToPageResult>();
    }
}
```

---

## Test Data Strategy

### Seed Data for Tests
**Use InMemory Database with test seed:**
```csharp
public static class TestDataSeeder
{
    public static void Seed(AppDbContext db)
    {
        var company = new Company { Id = 1, Name = "Test Co" };
        db.Companies.Add(company);
        
        var owner = new AppUser 
        { 
            Id = 1, 
            CompanyId = 1, 
            Email = "owner@test.com",
            Role = UserRole.Owner,
            IsActive = true
        };
        db.Users.Add(owner);
        
        var shiftType = new ShiftType
        {
            Id = 1,
            CompanyId = 1,
            Key = "MORNING",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        db.ShiftTypes.Add(shiftType);
        
        db.SaveChanges();
    }
}
```

### Multi-Tenant Test Scenarios
```csharp
public class MultiTenantTestBase
{
    protected AppDbContext CreateDbContext(int tenantId)
    {
        var tenantResolver = new Mock<ITenantResolver>();
        tenantResolver.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;
            
        return new AppDbContext(options, tenantResolver.Object);
    }
}
```

---

## Critical Test Scenarios

### 1. Multi-Tenant Isolation Tests _[CRITICAL]_
```csharp
[Fact]
public async Task Users_FromDifferentCompanies_NotVisible()
{
    // Arrange - Create users in companies 1 and 2
    // Act - Query users as company 1
    // Assert - Only company 1 users returned
}

[Fact]
public async Task CreateEntity_AutoInjectsCompanyId()
{
    // Arrange - Set tenant context to company 1
    // Act - Create shift instance without explicit CompanyId
    // Assert - Entity saved with CompanyId = 1
}
```

### 2. Director Multi-Company Tests _[CRITICAL]_
```csharp
[Fact]
public async Task Director_CanAccessAssignedCompanies()
{
    // Arrange - Director assigned to companies 1 and 2
    // Act - Query accessible company IDs
    // Assert - Returns [1, 2]
}

[Fact]
public async Task Director_CannotAssignOwnerRole()
{
    // Arrange - Director user
    // Act - Try to assign Owner role
    // Assert - Returns false / throws
}
```

### 3. API Authentication Tests _[CRITICAL]_
```csharp
[Fact]
public async Task InvalidApiKey_Returns401()
{
    // Arrange - Invalid API key
    // Act - Make API request
    // Assert - 401 Unauthorized
}

[Fact]
public async Task ExpiredApiKey_Returns403()
{
    // Arrange - Expired API key
    // Act - Make API request  
    // Assert - 403 Forbidden
}

[Fact]
public async Task RateLimitExceeded_Returns429()
{
    // Arrange - API key with rate limit 10/min
    // Act - Make 11 requests
    // Assert - 11th request returns 429
}
```

### 4. Conflict Detection Tests
```csharp
[Fact]
public async Task CreateChore_WhenUserHasShift_ReturnsConflict()
{
    // Arrange - User has MORNING shift on date
    // Act - Try to create chore on same date
    // Assert - Conflict error returned
}

[Fact]
public async Task AssignTrainee_WhenAlreadyAssigned_ReturnsError()
{
    // Arrange - Shift already has trainee
    // Act - Try to assign another trainee
    // Assert - Error returned
}
```

### 5. Password Hashing Tests
```csharp
[Fact]
public void CreateHash_ProducesValidHash()
{
    // Act
    var (hash, salt) = PasswordHasher.CreateHash("password123");
    
    // Assert
    hash.Should().NotBeEmpty();
    salt.Should().NotBeEmpty();
    hash.Length.Should().Be(64); // SHA256
}

[Fact]
public void Verify_WithCorrectPassword_ReturnsTrue()
{
    // Arrange
    var (hash, salt) = PasswordHasher.CreateHash("password123");
    
    // Act
    var result = PasswordHasher.Verify("password123", hash, salt);
    
    // Assert
    result.Should().BeTrue();
}
```

---

## Test Organization Recommendations

### Folder Structure
```
ShiftManager.Tests/
├── UnitTests/
│   ├── Services/
│   │   ├── DirectorServiceTests.cs     ✅ Exists
│   │   ├── TraineeServiceTests.cs      ❌ Needed
│   │   ├── ChoreServiceTests.cs        ❌ Needed
│   │   ├── ApiKeyServiceTests.cs       ❌ Needed
│   │   ├── NotificationServiceTests.cs ❌ Needed
│   │   └── TenantResolverTests.cs      ❌ Needed
│   ├── Models/
│   │   └── PasswordHasherTests.cs      ❌ Needed
│   └── Middleware/
│       ├── ApiAuthenticationTests.cs   ❌ Needed
│       └── CompanyContextTests.cs      ❌ Needed
├── IntegrationTests/
│   ├── Api/
│   │   ├── UsersControllerTests.cs     ❌ Needed
│   │   ├── ShiftsControllerTests.cs    ❌ Needed
│   │   └── AuthenticationFlowTests.cs  ❌ Needed
│   └── Pages/
│       ├── LoginPageTests.cs           ❌ Needed
│       └── CalendarPageTests.cs        ❌ Needed
├── TestFixtures/
│   ├── TestDataSeeder.cs               ❌ Needed
│   └── WebApplicationFactoryFixture.cs ❌ Needed
└── TestHelpers/
    ├── MockFactory.cs                  ❌ Needed
    └── AssertionExtensions.cs          ❌ Needed
```

---

## Test Coverage Goals

### Recommended Coverage Targets

| Layer | Current | Target | Priority |
|-------|---------|--------|----------|
| DirectorService | 100% | 100% | ✅ Met |
| Other Services | 0% | 80% | 🔴 Critical |
| Controllers | 0% | 70% | 🟡 High |
| Middleware | 0% | 90% | 🔴 Critical |
| Page Models | 0% | 60% | 🟢 Medium |
| Overall | ~5% | 70% | — |

### Testing Priorities Roadmap

**Phase 1: Security-Critical (Week 1)**
1. ✅ DirectorService tests (done)
2. ❌ ApiKeyService tests
3. ❌ ApiAuthenticationMiddleware tests
4. ❌ TenantResolver tests

**Phase 2: Core Business Logic (Week 2)**
1. ❌ TraineeService tests
2. ❌ ChoreService tests
3. ❌ OnDutyService tests
4. ❌ ConflictChecker tests

**Phase 3: Integration Tests (Week 3)**
1. ❌ Authentication flow tests
2. ❌ API endpoint tests
3. ❌ Multi-tenant isolation tests

**Phase 4: UI Tests (Week 4)**
1. ❌ Critical page model tests
2. ❌ Form validation tests

---

## Running Tests in CI/CD

### Recommended CI Configuration
```yaml
# .github/workflows/test.yml
name: Run Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage"
      - name: Upload coverage
        uses: codecov/codecov-action@v4
```

---

## Known Test Gaps (From Evidence)

| Gap | Risk Level | Evidence |
|-----|------------|----------|
| No TraineeService tests | 🔴 High | Trainee assignment could fail silently |
| No API authentication tests | 🔴 Critical | API security not verified |
| No multi-tenant isolation tests | 🔴 Critical | Cross-tenant data leaks possible |
| No ChoreService tests | 🟡 High | Conflict detection untested |
| No integration tests for API | 🟡 High | API contracts not verified |
| No page model tests | 🟢 Medium | UI logic untested |

---

## Test Documentation

### Existing Test Documentation
**Evidence:** `docs/` folder contains test-related files:
- `docs/COMPREHENSIVE_TEST_RESULTS_2026-01-06.md` (23KB)
- `docs/FINAL_TEST_REPORT_2026-01-06.md` (16KB)
- `docs/TEST_PLAN_MULTI_USER_MCP.md` (19KB)
- `docs/TEST_RESULTS_PHASE_3.3_COMPLETE_2026-01-04.md` (16KB)

### Test Result Files
**Evidence:** Root folder contains:
- `TEST_RESULTS_2026-01-06.json` (21KB)
- `test-results-2026-01-04.json` (20KB)
- `test-results.txt` (10KB)
- `test-output.log` (4KB)

❓ **Unverified:** Content of these test result files needs review to understand what tests were run.
