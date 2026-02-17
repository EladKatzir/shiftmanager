using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

[Authorize(Policy = "Grant:AssignChores")]
[IgnoreAntiforgeryToken]
public class RestoreChoreModel : PageModel
{
    private readonly IChoreService _choreService;
    private readonly ILogger<RestoreChoreModel> _logger;

    public RestoreChoreModel(IChoreService choreService, ILogger<RestoreChoreModel> logger)
    {
        _choreService = choreService;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<RestoreRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid chore ID" })
                    { StatusCode = 400 };
            }

            var result = await _choreService.RestoreChoreAsync(data.Id);

            return new JsonResult(new { success = result.Success, message = result.Message })
                { StatusCode = result.Success ? 200 : 400 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring chore via undo");
            return new JsonResult(new { success = false, message = "An error occurred" })
                { StatusCode = 500 };
        }
    }

    private class RestoreRequest
    {
        public int Id { get; set; }
    }
}
