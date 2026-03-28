using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class ApiKeyServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ApiKeyService _service;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<IAuditLogService> _auditLogServiceMock;

    private const int CompanyId = 1;
    private const int UserId = 1;
    private const int ReviewerId = 2;

    public ApiKeyServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);

        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyId);

        _auditLogServiceMock = new Mock<IAuditLogService>();
        _auditLogServiceMock.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _service = new ApiKeyService(
            _db,
            _auditLogServiceMock.Object,
            _tenantResolverMock.Object,
            Mock.Of<ILogger<ApiKeyService>>());

        SeedBaseData();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedBaseData()
    {
        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = 1, Name = "TestCo", DisplayName = "Test Company" });
        _db.Users.AddRange(
            new AppUser { Id = UserId, Email = "user@test.com", DisplayName = "User", CompanyId = CompanyId, IsActive = true },
            new AppUser { Id = ReviewerId, Email = "reviewer@test.com", DisplayName = "Reviewer", CompanyId = CompanyId, IsActive = true, Role = UserRole.Manager }
        );
        _db.SaveChanges();
    }

    // --- RequestApiKeyAsync ---

    [Fact]
    public async Task RequestApiKeyAsync_HappyPath_CreatesRequest()
    {
        var (request, error) = await _service.RequestApiKeyAsync(CompanyId, UserId, "My Key", "For integration", "user:read");

        error.Should().BeNull();
        request.Should().NotBeNull();
        request!.Name.Should().Be("My Key");
        request.Description.Should().Be("For integration");
        request.RequestedScopes.Should().Be("user:read");
        request.Status.Should().Be(ApiKeyRequestStatus.Pending);
        request.RequestedBy.Should().Be(UserId);
        request.CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public async Task RequestApiKeyAsync_EmptyName_ReturnsError()
    {
        var (request, error) = await _service.RequestApiKeyAsync(CompanyId, UserId, "", "Desc", "user:read");

        request.Should().BeNull();
        error.Should().Contain("name is required");
    }

    [Fact]
    public async Task RequestApiKeyAsync_EmptyDescription_ReturnsError()
    {
        var (request, error) = await _service.RequestApiKeyAsync(CompanyId, UserId, "Key", "", "user:read");

        request.Should().BeNull();
        error.Should().Contain("Description is required");
    }

    [Fact]
    public async Task RequestApiKeyAsync_EmptyScopes_ReturnsError()
    {
        var (request, error) = await _service.RequestApiKeyAsync(CompanyId, UserId, "Key", "Desc", "");

        request.Should().BeNull();
        error.Should().Contain("scope");
    }

    [Fact]
    public async Task RequestApiKeyAsync_DuplicatePendingRequest_ReturnsError()
    {
        await _service.RequestApiKeyAsync(CompanyId, UserId, "My Key", "Desc", "user:read");

        var (request, error) = await _service.RequestApiKeyAsync(CompanyId, UserId, "My Key", "Another Desc", "shift:read");

        request.Should().BeNull();
        error.Should().Contain("already have a pending request");
    }

    [Fact]
    public async Task RequestApiKeyAsync_LogsAudit()
    {
        await _service.RequestApiKeyAsync(CompanyId, UserId, "My Key", "Desc", "user:read");

        _auditLogServiceMock.Verify(a => a.LogAsync(
            "ApiKeyRequest.Created",
            "ApiKeyRequest",
            It.IsAny<int?>(),
            It.Is<string>(s => s.Contains("My Key")),
            It.IsAny<string?>()), Times.Once);
    }

    // --- ListPendingRequestsAsync ---

    [Fact]
    public async Task ListPendingRequestsAsync_ReturnsOnlyPending()
    {
        await _service.RequestApiKeyAsync(CompanyId, UserId, "Pending1", "Desc", "user:read");
        await _service.RequestApiKeyAsync(CompanyId, UserId, "Pending2", "Desc", "shift:read");

        // Approve one
        var requests = await _db.ApiKeyRequests.ToListAsync();
        requests[0].Status = ApiKeyRequestStatus.Rejected;
        await _db.SaveChangesAsync();

        var pending = await _service.ListPendingRequestsAsync(CompanyId);

        pending.Should().HaveCount(1);
        pending[0].Name.Should().Be("Pending2");
    }

    // --- ListAllRequestsAsync ---

    [Fact]
    public async Task ListAllRequestsAsync_ReturnsPaginated()
    {
        for (int i = 0; i < 5; i++)
        {
            await _service.RequestApiKeyAsync(CompanyId, UserId, $"Key{i}", "Desc", "user:read");
        }

        var page1 = await _service.ListAllRequestsAsync(CompanyId, page: 1, pageSize: 3);
        var page2 = await _service.ListAllRequestsAsync(CompanyId, page: 2, pageSize: 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }

    // --- ListUserRequestsAsync ---

    [Fact]
    public async Task ListUserRequestsAsync_ReturnsOnlyUserRequests()
    {
        await _service.RequestApiKeyAsync(CompanyId, UserId, "UserKey", "Desc", "user:read");
        await _service.RequestApiKeyAsync(CompanyId, ReviewerId, "ReviewerKey", "Desc", "user:read");

        var result = await _service.ListUserRequestsAsync(CompanyId, UserId);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("UserKey");
    }

    // --- ApproveRequestAsync ---

    [Fact]
    public async Task ApproveRequestAsync_HappyPath_GeneratesKeyAndUpdatesStatus()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");

        var (apiKey, request, error) = await _service.ApproveRequestAsync(req!.Id, ReviewerId,
            reviewNotes: "Approved for testing");

        error.Should().BeNull();
        apiKey.Should().NotBeNull();
        apiKey.Should().StartWith("sk_");
        request.Should().NotBeNull();
        request!.Status.Should().Be(ApiKeyRequestStatus.Approved);
        request.ReviewedBy.Should().Be(ReviewerId);
        request.ReviewedAt.Should().NotBeNull();
        request.ReviewNotes.Should().Be("Approved for testing");
        request.GeneratedApiKeyId.Should().NotBeNull();
    }

    [Fact]
    public async Task ApproveRequestAsync_SetsCustomScopesAndRateLimit()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");

        var (apiKey, request, error) = await _service.ApproveRequestAsync(req!.Id, ReviewerId,
            approvedScopes: "user:read,shift:read",
            rateLimitPerMinute: 50,
            expiresAt: new DateTime(2027, 1, 1));

        error.Should().BeNull();
        request!.ApprovedScopes.Should().Be("user:read,shift:read");
        request.ApprovedRateLimit.Should().Be(50);
        request.ApprovedExpiresAt.Should().Be(new DateTime(2027, 1, 1));

        // Verify the API key record
        var key = await _db.ApiKeys.FindAsync(request.GeneratedApiKeyId);
        key!.Scopes.Should().Be("user:read,shift:read");
        key.RateLimitPerMinute.Should().Be(50);
        key.ExpiresAt.Should().Be(new DateTime(2027, 1, 1));
    }

    [Fact]
    public async Task ApproveRequestAsync_RequestNotFound_ReturnsError()
    {
        var (_, _, error) = await _service.ApproveRequestAsync(999, ReviewerId);

        error.Should().Contain("not found");
    }

    [Fact]
    public async Task ApproveRequestAsync_AlreadyApproved_ReturnsError()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");
        await _service.ApproveRequestAsync(req!.Id, ReviewerId);

        var (_, _, error) = await _service.ApproveRequestAsync(req.Id, ReviewerId);

        error.Should().Contain("not pending");
    }

    // --- RejectRequestAsync ---

    [Fact]
    public async Task RejectRequestAsync_HappyPath_UpdatesStatus()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");

        var (request, error) = await _service.RejectRequestAsync(req!.Id, ReviewerId, "Not needed");

        error.Should().BeNull();
        request.Should().NotBeNull();
        request!.Status.Should().Be(ApiKeyRequestStatus.Rejected);
        request.ReviewedBy.Should().Be(ReviewerId);
        request.ReviewNotes.Should().Be("Not needed");
    }

    [Fact]
    public async Task RejectRequestAsync_EmptyReason_ReturnsError()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");

        var (request, error) = await _service.RejectRequestAsync(req!.Id, ReviewerId, "");

        request.Should().BeNull();
        error.Should().Contain("Rejection reason is required");
    }

    [Fact]
    public async Task RejectRequestAsync_AlreadyRejected_ReturnsError()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");
        await _service.RejectRequestAsync(req!.Id, ReviewerId, "No");

        var (_, error) = await _service.RejectRequestAsync(req.Id, ReviewerId, "No again");

        error.Should().Contain("not pending");
    }

    // --- RevokeApiKeyAsync ---

    [Fact]
    public async Task RevokeApiKeyAsync_HappyPath_DeactivatesKey()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");
        var (_, request, _) = await _service.ApproveRequestAsync(req!.Id, ReviewerId);

        var (revokedKey, error) = await _service.RevokeApiKeyAsync(request!.GeneratedApiKeyId!.Value, ReviewerId, "No longer needed");

        error.Should().BeNull();
        revokedKey.Should().NotBeNull();
        revokedKey!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeApiKeyAsync_AlreadyInactive_ReturnsError()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");
        var (_, request, _) = await _service.ApproveRequestAsync(req!.Id, ReviewerId);
        var keyId = request!.GeneratedApiKeyId!.Value;

        await _service.RevokeApiKeyAsync(keyId, ReviewerId);
        var (_, error) = await _service.RevokeApiKeyAsync(keyId, ReviewerId);

        error.Should().Contain("already inactive");
    }

    [Fact]
    public async Task RevokeApiKeyAsync_NotFound_ReturnsError()
    {
        var (_, error) = await _service.RevokeApiKeyAsync(999, ReviewerId);

        error.Should().Contain("not found");
    }

    // --- ListAllKeysAsync ---

    [Fact]
    public async Task ListAllKeysAsync_ReturnsOnlyActiveByDefault()
    {
        var (req1, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "Key1", "Desc", "user:read");
        var (req2, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "Key2", "Desc", "shift:read");
        var (_, request1, _) = await _service.ApproveRequestAsync(req1!.Id, ReviewerId);
        var (_, request2, _) = await _service.ApproveRequestAsync(req2!.Id, ReviewerId);

        // Revoke one
        await _service.RevokeApiKeyAsync(request1!.GeneratedApiKeyId!.Value, ReviewerId);

        var activeKeys = await _service.ListAllKeysAsync(CompanyId);

        activeKeys.Should().HaveCount(1);
        activeKeys[0].Name.Should().Be("Key2");
    }

    [Fact]
    public async Task ListAllKeysAsync_IncludeInactive_ReturnsAll()
    {
        var (req1, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "Key1", "Desc", "user:read");
        var (req2, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "Key2", "Desc", "shift:read");
        var (_, request1, _) = await _service.ApproveRequestAsync(req1!.Id, ReviewerId);
        await _service.ApproveRequestAsync(req2!.Id, ReviewerId);

        await _service.RevokeApiKeyAsync(request1!.GeneratedApiKeyId!.Value, ReviewerId);

        var allKeys = await _service.ListAllKeysAsync(CompanyId, includeInactive: true);

        allKeys.Should().HaveCount(2);
    }

    // --- ListUserKeysAsync ---

    [Fact]
    public async Task ListUserKeysAsync_ReturnsActiveKeysForUser()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "UserKey", "Desc", "user:read");
        await _service.ApproveRequestAsync(req!.Id, ReviewerId);

        // Another user's key
        var (req2, _) = await _service.RequestApiKeyAsync(CompanyId, ReviewerId, "ReviewerKey", "Desc", "admin:read");
        await _service.ApproveRequestAsync(req2!.Id, ReviewerId);

        var result = await _service.ListUserKeysAsync(CompanyId, UserId);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("UserKey");
    }

    // --- GetRequestByIdAsync ---

    [Fact]
    public async Task GetRequestByIdAsync_ExistingRequest_ReturnsWithIncludes()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");

        var result = await _service.GetRequestByIdAsync(req!.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("TestKey");
    }

    [Fact]
    public async Task GetRequestByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.GetRequestByIdAsync(999);

        result.Should().BeNull();
    }

    // --- GetKeyByIdAsync ---

    [Fact]
    public async Task GetKeyByIdAsync_ExistingKey_Returns()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");
        var (_, request, _) = await _service.ApproveRequestAsync(req!.Id, ReviewerId);

        var result = await _service.GetKeyByIdAsync(request!.GeneratedApiKeyId!.Value);

        result.Should().NotBeNull();
        result!.Name.Should().Be("TestKey");
    }

    [Fact]
    public async Task GetKeyByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.GetKeyByIdAsync(999);

        result.Should().BeNull();
    }

    // --- RegenerateApiKeyAsync ---

    [Fact]
    public async Task RegenerateApiKeyAsync_HappyPath_GeneratesNewKey()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");
        var (originalKey, request, _) = await _service.ApproveRequestAsync(req!.Id, ReviewerId);
        var keyId = request!.GeneratedApiKeyId!.Value;

        var (newKey, error) = await _service.RegenerateApiKeyAsync(keyId, ReviewerId);

        error.Should().BeNull();
        newKey.Should().NotBeNull();
        newKey.Should().StartWith("sk_");
        newKey.Should().NotBe(originalKey); // New key should be different
    }

    [Fact]
    public async Task RegenerateApiKeyAsync_InactiveKey_ReturnsError()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");
        var (_, request, _) = await _service.ApproveRequestAsync(req!.Id, ReviewerId);
        var keyId = request!.GeneratedApiKeyId!.Value;

        await _service.RevokeApiKeyAsync(keyId, ReviewerId);

        var (_, error) = await _service.RegenerateApiKeyAsync(keyId, ReviewerId);

        error.Should().Contain("inactive");
    }

    [Fact]
    public async Task RegenerateApiKeyAsync_NotFound_ReturnsError()
    {
        var (_, error) = await _service.RegenerateApiKeyAsync(999, ReviewerId);

        error.Should().Contain("not found");
    }

    [Fact]
    public async Task RegenerateApiKeyAsync_ResetsLastUsedAt()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, UserId, "TestKey", "Desc", "user:read");
        var (_, request, _) = await _service.ApproveRequestAsync(req!.Id, ReviewerId);
        var keyId = request!.GeneratedApiKeyId!.Value;

        // Simulate usage
        var key = await _db.ApiKeys.FindAsync(keyId);
        key!.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _service.RegenerateApiKeyAsync(keyId, ReviewerId);

        var refreshed = await _db.ApiKeys.FindAsync(keyId);
        refreshed!.LastUsedAt.Should().BeNull();
    }
}
