namespace ShiftManager.Migrations;

/// <summary>
/// Backfill SQL shared between the BackfillCompanyMembershipsFromPrimary migration and its
/// regression test, so both run identical statements (same idiom as ShiftCategoryBackfillSql).
/// Creates exactly one IsPrimary membership per existing user, copying the per-company columns
/// off Users. Idempotent: the NOT EXISTS guard makes a re-run a no-op.
/// </summary>
public static class CompanyMembershipBackfillSql
{
    public static readonly string[] Forward = new[]
    {
        @"INSERT INTO CompanyMemberships
              (UserId, CompanyId, RoleTemplateId, JobTypeId, DepartmentId, DoesShifts, HomeTypeId,
               IsPrimary, IsDeleted, DeletedAt, JoinedAt, GrantedBy)
          SELECT u.Id, u.CompanyId, u.RoleTemplateId, u.JobTypeId, u.DepartmentId, u.DoesShifts, u.HomeTypeId,
                 1, 0, NULL, datetime('now'), 0
          FROM Users u
          -- Guard is intentionally user-level (not per-company): protects the one-primary-per-user invariant on re-run.
          WHERE NOT EXISTS (
              SELECT 1 FROM CompanyMemberships cm
              WHERE cm.UserId = u.Id AND cm.IsPrimary = 1 AND cm.IsDeleted = 0
          );"
    };
}
