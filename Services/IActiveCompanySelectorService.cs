namespace ShiftManager.Services;

/// <summary>
/// Lets a multi-company member choose which company is their active tenant. The authoritative
/// membership check happens here (async, DB) before the cookie is written; TenantResolver then
/// validates the cookie synchronously against the MemberCompanyIds claim. Mirrors
/// IOwnerCompanySelectorService but gated by CompanyMembership rather than the Owner role.
/// </summary>
public interface IActiveCompanySelectorService
{
    int? GetSelectedCompanyId();
    Task<bool> SelectCompanyAsync(int companyId);
    Task ClearSelectionAsync();
}
