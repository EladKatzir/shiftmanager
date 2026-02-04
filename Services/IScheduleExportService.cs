using ShiftManager.Models.Export;

namespace ShiftManager.Services;

/// <summary>
/// Service for exporting schedule data to various formats (PDF, Excel, CSV).
/// </summary>
public interface IScheduleExportService
{
    /// <summary>
    /// Collects schedule data for the specified date range and filters.
    /// </summary>
    /// <param name="request">Export request containing date range and filter options.</param>
    /// <returns>Collected export data ready for formatting.</returns>
    Task<ScheduleExportData> CollectExportDataAsync(ScheduleExportRequest request);

    /// <summary>
    /// Generates a PDF document from the collected export data.
    /// Uses QuestPDF for document generation.
    /// </summary>
    /// <param name="data">The export data to render as PDF.</param>
    /// <returns>PDF file bytes.</returns>
    Task<byte[]> GeneratePdfAsync(ScheduleExportData data);

    /// <summary>
    /// Generates an Excel workbook from the collected export data.
    /// Uses ClosedXML for workbook generation.
    /// </summary>
    /// <param name="data">The export data to render as Excel.</param>
    /// <returns>Excel file bytes (.xlsx).</returns>
    Task<byte[]> GenerateExcelAsync(ScheduleExportData data);

    /// <summary>
    /// Generates a CSV file from the collected export data.
    /// </summary>
    /// <param name="data">The export data to render as CSV.</param>
    /// <returns>CSV file bytes (UTF-8 encoded).</returns>
    Task<byte[]> GenerateCsvAsync(ScheduleExportData data);
}
