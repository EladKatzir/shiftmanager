using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Models.Export;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api;

[Authorize]
[IgnoreAntiforgeryToken]
public class ScheduleExportModel : PageModel
{
    private readonly IScheduleExportService _exportService;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public ScheduleExportModel(IScheduleExportService exportService, IStringLocalizer<SharedResources> localizer)
    {
        _exportService = exportService;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnPostAsync([FromBody] ScheduleExportRequest request)
    {
        if (request == null)
            return BadRequest(_localizer["Error_InvalidExportRequest"].Value);

        // Validate date range to prevent excessive resource consumption
        if ((request.EndDate - request.StartDate).Days > 365)
        {
            return new JsonResult(new { success = false, message = "ScheduleExport_DateRangeExceeded" }) { StatusCode = 400 };
        }

        var data = await _exportService.CollectExportDataAsync(request);

        byte[] fileBytes;
        string contentType;
        string fileName;

        switch (request.Format)
        {
            case ExportFormat.Pdf:
                fileBytes = await _exportService.GeneratePdfAsync(data);
                contentType = "application/pdf";
                fileName = $"schedule-{data.StartDate:yyyy-MM-dd}.pdf";
                break;

            case ExportFormat.Excel:
                fileBytes = await _exportService.GenerateExcelAsync(data);
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                fileName = $"schedule-{data.StartDate:yyyy-MM-dd}.xlsx";
                break;

            case ExportFormat.Csv:
                fileBytes = await _exportService.GenerateCsvAsync(data);
                contentType = "text/csv";
                fileName = $"schedule-{data.StartDate:yyyy-MM-dd}.csv";
                break;

            default:
                return BadRequest(_localizer["Error_UnsupportedExportFormat"].Value);
        }

        return File(fileBytes, contentType, fileName);
    }
}
