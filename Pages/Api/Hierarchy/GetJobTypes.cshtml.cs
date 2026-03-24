using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api.Hierarchy;

[Authorize]
public class GetJobTypesModel : PageModel
{
    private readonly IJobTypeService _jobTypeService;

    public GetJobTypesModel(IJobTypeService jobTypeService)
    {
        _jobTypeService = jobTypeService;
    }

    public async Task<IActionResult> OnGetAsync(int moleculeId)
    {
        if (moleculeId <= 0)
            return new JsonResult(new { error = "Invalid molecule ID" }) { StatusCode = 400 };

        var jobTypes = await _jobTypeService.GetJobTypesForMoleculeAsync(moleculeId);
        return new JsonResult(new
        {
            jobTypes = jobTypes.Select(jt => new
            {
                id = jt.Id,
                name = jt.DisplayName ?? jt.Name,
                key = jt.Name
            })
        });
    }
}
