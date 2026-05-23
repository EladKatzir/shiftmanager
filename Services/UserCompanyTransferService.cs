using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Services;

public class UserCompanyTransferService : IUserCompanyTransferService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IConcurrencyService _concurrencyService;
    private readonly IAuditLogService _auditLogService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<UserCompanyTransferService> _logger;

    public UserCompanyTransferService(
        AppDbContext db,
        IGrantService grantService,
        IConcurrencyService concurrencyService,
        IAuditLogService auditLogService,
        IWebHostEnvironment env,
        ILogger<UserCompanyTransferService> logger)
    {
        _db = db;
        _grantService = grantService;
        _concurrencyService = concurrencyService;
        _auditLogService = auditLogService;
        _env = env;
        _logger = logger;
    }

    public Task<MoveImpact> GetMoveImpactAsync(int userId, int destCompanyId)
        => throw new NotImplementedException();

    public Task<MoveResult> MoveUserToCompanyAsync(int userId, int destCompanyId, int actingAdminId)
        => throw new NotImplementedException();
}
