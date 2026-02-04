using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Models.Export;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api;

[Authorize]
[IgnoreAntiforgeryToken]
public class ScheduleExportModel : PageModel
{
    private readonly IScheduleExportService _exportService;

    public ScheduleExportModel(IScheduleExportService exportService)
    {
        _exportService = exportService;
    }

    public async Task<IActionResult> OnPostAsync([FromBody] ScheduleExportRequest request)
    {
        if (request == null)
            return BadRequest("Invalid export request");

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
                return BadRequest("Unsupported export format");
        }

        return File(fileBytes, contentType, fileName);
    }
}
