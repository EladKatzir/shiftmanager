using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Regression suite for the API-key theft defect (whole-app audit, finding B-F01).
///
/// THE BUG: <c>/My/ApiKeys</c> is bare <c>[Authorize]</c> and its page model loads
/// <c>ListAllKeysAsync(companyId)</c> — EVERY key in the company is listed to EVERY user, so the key
/// ids are handed to the caller by the UI itself. <c>RegenerateApiKeyAsync</c> and
/// <c>RevokeApiKeyAsync</c> then filtered ONLY on <c>k.CompanyId == companyId</c>, never on
/// ownership; the acting user id was used solely for the log line. So any authenticated user —
/// including a Trainee — could POST <c>handler=Refresh&amp;keyId=&lt;a colleague's key&gt;</c>,
/// which ROTATED the victim's key (breaking their integration) and RETURNED the new plaintext, which
/// the page renders with a copy-to-clipboard button. Credential theft and denial of service in one
/// request.
///
/// THE OWNERSHIP TRAP (why the obvious fix is wrong): <c>ApiKey.CreatedBy</c> is the APPROVING
/// REVIEWER, not the owner — <c>ApiKeyService</c> sets
/// <c>CreatedBy = reviewerId, // Reviewer creates it on behalf of requester</c>. Adding
/// <c>k.CreatedBy == callerId</c> would have LOCKED THE REAL OWNER OUT of their own key and left only
/// the approving admin able to rotate it. The true owner is the user whose approved request generated
/// the key: <c>ApiKeyRequest.GeneratedApiKeyId == key.Id</c> → <c>ApiKeyRequest.RequestedBy</c>.
///
/// The tests below therefore pin BOTH directions: the attacker is refused AND the legitimate owner
/// (and an admin) still succeed. Availability is as much the contract here as authorization.
/// </summary>
public class ApiKeyOwnershipTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly ApiKeyService _service;

    private const int CompanyId = 1;
    private const int OwnerUserId = 1;    // requests the key -> the true owner
    private const int ReviewerId = 2;     // approves it -> ends up in ApiKey.CreatedBy
    private const int AttackerId = 3;     // same company, no relationship to the key

    public ApiKeyOwnershipTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var tenant = new Mock<ITenantResolver>();
        tenant.Setup(t => t.GetCurrentTenantId()).Returns(CompanyId);
        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _service = new ApiKeyService(_db, audit.Object, tenant.Object, Mock.Of<ILogger<ApiKeyService>>());

        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = 1, Name = "TestCo", DisplayName = "Test Company" });
        _db.Users.AddRange(
            new AppUser { Id = OwnerUserId, Email = "owner@test.com", DisplayName = "Owner", CompanyId = CompanyId, IsActive = true },
            new AppUser { Id = ReviewerId, Email = "reviewer@test.com", DisplayName = "Reviewer", CompanyId = CompanyId, IsActive = true, Role = UserRole.Manager },
            new AppUser { Id = AttackerId, Email = "attacker@test.com", DisplayName = "Attacker", CompanyId = CompanyId, IsActive = true });
        _db.SaveChanges();
    }

    /// <summary>Requests a key as <see cref="OwnerUserId"/> and approves it as the reviewer.</summary>
    private async Task<int> CreateApprovedKeyForOwnerAsync()
    {
        var (req, _) = await _service.RequestApiKeyAsync(CompanyId, OwnerUserId, "Owner Key", "desc", "user:read");
        var (_, request, _) = await _service.ApproveRequestAsync(req!.Id, ReviewerId);
        return request!.GeneratedApiKeyId!.Value;
    }

    // ---------------------------------------------------------------------------------
    // The defect: a colleague must not be able to rotate (and thereby read) someone's key
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Regenerate_ByUnrelatedUserInSameCompany_IsRefusedAndDoesNotRotateTheKey()
    {
        var keyId = await CreateApprovedKeyForOwnerAsync();
        var before = await _db.ApiKeys.AsNoTracking().FirstAsync(k => k.Id == keyId);

        var (newKey, error) = await _service.RegenerateApiKeyAsync(keyId, AttackerId);

        error.Should().NotBeNull();
        newKey.Should().BeNull("the plaintext must never be handed to a non-owner");

        var after = await _db.ApiKeys.AsNoTracking().FirstAsync(k => k.Id == keyId);
        after.KeyHash.Should().Be(before.KeyHash, "the victim's key must not be rotated — that alone is a denial of service");
    }

    [Fact]
    public async Task Revoke_ByUnrelatedUserInSameCompany_IsRefusedAndLeavesKeyActive()
    {
        var keyId = await CreateApprovedKeyForOwnerAsync();

        var (_, error) = await _service.RevokeApiKeyAsync(keyId, AttackerId, "malicious");

        error.Should().NotBeNull();
        var after = await _db.ApiKeys.AsNoTracking().FirstAsync(k => k.Id == keyId);
        after.IsActive.Should().BeTrue("a colleague must not be able to disable someone else's integration");
    }

    // ---------------------------------------------------------------------------------
    // The availability half — the fix must NOT lock legitimate users out.
    // ApiKey.CreatedBy is the REVIEWER, so a naive CreatedBy check would fail this test.
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Regenerate_ByTheRequestingOwner_Succeeds()
    {
        var keyId = await CreateApprovedKeyForOwnerAsync();

        var (newKey, error) = await _service.RegenerateApiKeyAsync(keyId, OwnerUserId);

        error.Should().BeNull("the user who requested the key owns it, even though CreatedBy is the reviewer");
        newKey.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Regenerate_ByAdmin_Succeeds()
    {
        var keyId = await CreateApprovedKeyForOwnerAsync();

        var (newKey, error) = await _service.RegenerateApiKeyAsync(keyId, ReviewerId, callerIsAdmin: true);

        error.Should().BeNull("an administrator must retain the ability to rotate a key");
        newKey.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Revoke_ByTheRequestingOwner_Succeeds()
    {
        var keyId = await CreateApprovedKeyForOwnerAsync();

        var (_, error) = await _service.RevokeApiKeyAsync(keyId, OwnerUserId, "no longer needed");

        error.Should().BeNull();
        var after = await _db.ApiKeys.AsNoTracking().FirstAsync(k => k.Id == keyId);
        after.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_ByAdmin_Succeeds()
    {
        var keyId = await CreateApprovedKeyForOwnerAsync();

        var (_, error) = await _service.RevokeApiKeyAsync(keyId, ReviewerId, "policy", callerIsAdmin: true);

        error.Should().BeNull("the admin revoke path must keep working");
        var after = await _db.ApiKeys.AsNoTracking().FirstAsync(k => k.Id == keyId);
        after.IsActive.Should().BeFalse();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
