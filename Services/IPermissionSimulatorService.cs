using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface IPermissionSimulatorService
{
    Task<PermissionSimulationResult> SimulateUserAccessAsync(
        int userId,
        string grantKey,
        int? projectId    = null,
        int? areaId       = null,
        int? moleculeId   = null,
        int? departmentId = null,
        int? companyId    = null,
        int? jobTypeId    = null,
        int? targetUserId = null);

    Task<PermissionSimulationResult> SimulateRoleAccessAsync(
        int roleTemplateId,
        int? scopeJobTypeId,
        int? scopeCompanyId,
        int? scopeMoleculeId,
        int? scopeAreaId,
        string grantKey,
        int? projectId    = null,
        int? areaId       = null,
        int? moleculeId   = null,
        int? departmentId = null,
        int? companyId    = null,
        int? jobTypeId    = null);
}
