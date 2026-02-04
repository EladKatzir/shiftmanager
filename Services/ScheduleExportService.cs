using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Export;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Service for exporting schedule data to PDF, Excel, and CSV formats.
/// </summary>
public class ScheduleExportService : IScheduleExportService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<ScheduleExportService> _logger;

    public ScheduleExportService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        ILogger<ScheduleExportService> logger)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _logger = logger;

        // Set QuestPDF license for community use
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public async Task<ScheduleExportData> CollectExportDataAsync(ScheduleExportRequest request)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Get company name
        var company = await _db.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == companyId);

        var companyName = company?.DisplayName ?? company?.Name ?? "Unknown Company";

        // Get optional filter names
        string? departmentName = null;
        string? jobTypeName = null;

        if (request.DepartmentId.HasValue)
        {
            var department = await _db.Departments
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value);
            departmentName = department?.DisplayName ?? department?.Name;
        }

        if (request.JobTypeId.HasValue)
        {
            var jobType = await _db.JobTypes
                .FirstOrDefaultAsync(jt => jt.Id == request.JobTypeId.Value);
            jobTypeName = jobType?.DisplayName ?? jobType?.Name;
        }

        // Convert dates to DateOnly for querying
        var startDate = DateOnly.FromDateTime(request.StartDate);
        var endDate = DateOnly.FromDateTime(request.EndDate);

        // Query shift instances with assignments
        var shiftInstancesQuery = _db.ShiftInstances
            .Include(si => si.ShiftType)
            .Where(si => si.WorkDate >= startDate && si.WorkDate <= endDate);

        // Apply job type filter if specified
        if (request.JobTypeId.HasValue)
        {
            shiftInstancesQuery = shiftInstancesQuery
                .Where(si => si.ShiftType.JobTypeId == request.JobTypeId.Value);
        }

        var shiftInstances = await shiftInstancesQuery
            .OrderBy(si => si.WorkDate)
            .ThenBy(si => si.ShiftType.SortOrder)
            .ToListAsync();

        // Get assignments for these shift instances
        var shiftInstanceIds = shiftInstances.Select(si => si.Id).ToList();
        var assignments = await _db.ShiftAssignments
            .Include(sa => sa.User)
            .Where(sa => shiftInstanceIds.Contains(sa.ShiftInstanceId))
            .ToListAsync();

        // Query chores if requested
        List<Chore> chores = new();
        if (request.IncludeChores)
        {
            chores = await _db.Chores
                .Include(c => c.User)
                .Where(c => c.Date >= startDate && c.Date <= endDate && c.CanceledAt == null)
                .OrderBy(c => c.Date)
                .ToListAsync();
        }

        // Query on-duty assignments if requested
        // OnDuty is a global table (no query filters), so we need to filter by date only
        List<OnDuty> onDuties = new();
        if (request.IncludeOnDuty)
        {
            onDuties = await _db.OnDuties
                .Include(od => od.User)
                .Where(od => od.Date >= startDate && od.Date <= endDate && od.CanceledAt == null)
                .OrderBy(od => od.Date)
                .ThenBy(od => od.Type)
                .ToListAsync();
        }

        // Build export data structure
        var exportData = new ScheduleExportData
        {
            CompanyName = companyName,
            GeneratedAt = DateTime.UtcNow,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DepartmentName = departmentName,
            JobTypeName = jobTypeName,
            Days = new List<ExportDayData>()
        };

        // Group data by date
        var currentDate = startDate;
        while (currentDate <= endDate)
        {
            var dayData = new ExportDayData
            {
                Date = currentDate.ToDateTime(TimeOnly.MinValue),
                DayName = currentDate.DayOfWeek.ToString(),
                Shifts = new List<ExportShiftData>(),
                Chores = new List<ExportChoreData>(),
                OnDuties = new List<ExportOnDutyData>()
            };

            // Add shifts for this day
            var dayShifts = shiftInstances.Where(si => si.WorkDate == currentDate).ToList();
            foreach (var shift in dayShifts)
            {
                var shiftAssignments = assignments
                    .Where(a => a.ShiftInstanceId == shift.Id && a.UserId.HasValue)
                    .ToList();

                var shiftData = new ExportShiftData
                {
                    ShiftName = !string.IsNullOrEmpty(shift.Name) ? shift.Name : shift.ShiftType.Name,
                    TimeRange = FormatTimeRange(shift.ShiftType.Start, shift.ShiftType.End),
                    RequiredStaff = shift.StaffingRequired,
                    AssignedStaff = shiftAssignments.Count,
                    AssignedEmployees = request.IncludeEmployeeNames
                        ? shiftAssignments
                            .Where(a => a.User != null)
                            .Select(a => a.User!.DisplayName)
                            .OrderBy(name => name)
                            .ToList()
                        : new List<string>()
                };

                dayData.Shifts.Add(shiftData);
            }

            // Add chores for this day
            if (request.IncludeChores)
            {
                var dayChores = chores.Where(c => c.Date == currentDate).ToList();
                foreach (var chore in dayChores)
                {
                    dayData.Chores.Add(new ExportChoreData
                    {
                        ChoreName = chore.Title,
                        AssignedTo = request.IncludeEmployeeNames ? chore.User?.DisplayName : null,
                        IsCompleted = false // Chores don't have a completion status in the current model
                    });
                }
            }

            // Add on-duty assignments for this day
            if (request.IncludeOnDuty)
            {
                var dayOnDuties = onDuties.Where(od => od.Date == currentDate).ToList();
                foreach (var onDuty in dayOnDuties)
                {
                    dayData.OnDuties.Add(new ExportOnDutyData
                    {
                        DutyName = FormatOnDutyType(onDuty.Type),
                        AssignedTo = request.IncludeEmployeeNames ? onDuty.User?.DisplayName : null
                    });
                }
            }

            exportData.Days.Add(dayData);
            currentDate = currentDate.AddDays(1);
        }

        _logger.LogInformation(
            "Collected export data for company {CompanyId}: {DayCount} days, {ShiftCount} shifts",
            companyId,
            exportData.Days.Count,
            exportData.Days.Sum(d => d.Shifts.Count));

        return exportData;
    }

    /// <inheritdoc />
    public Task<byte[]> GeneratePdfAsync(ScheduleExportData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header()
                    .Column(column =>
                    {
                        column.Item().Text(data.CompanyName)
                            .FontSize(16)
                            .Bold()
                            .AlignCenter();

                        column.Item().Text($"Schedule Export: {data.StartDate:yyyy-MM-dd} to {data.EndDate:yyyy-MM-dd}")
                            .FontSize(12)
                            .AlignCenter();

                        if (!string.IsNullOrEmpty(data.DepartmentName) || !string.IsNullOrEmpty(data.JobTypeName))
                        {
                            var filterText = new List<string>();
                            if (!string.IsNullOrEmpty(data.DepartmentName))
                                filterText.Add($"Department: {data.DepartmentName}");
                            if (!string.IsNullOrEmpty(data.JobTypeName))
                                filterText.Add($"Job Type: {data.JobTypeName}");

                            column.Item().Text(string.Join(" | ", filterText))
                                .FontSize(10)
                                .AlignCenter();
                        }

                        column.Item().PaddingTop(10);
                    });

                page.Content()
                    .Table(table =>
                    {
                        // Define columns
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(80);  // Day/Date
                            columns.RelativeColumn(2);   // Shift
                            columns.ConstantColumn(60);  // Time
                            columns.ConstantColumn(50);  // Staff
                            columns.RelativeColumn(3);   // Assigned
                        });

                        // Header row
                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(5)
                                .Text("Date").Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(5)
                                .Text("Shift").Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(5)
                                .Text("Time").Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(5)
                                .Text("Staff").Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(5)
                                .Text("Assigned").Bold();
                        });

                        // Data rows
                        foreach (var day in data.Days)
                        {
                            var isFirstShiftForDay = true;

                            if (day.Shifts.Count == 0)
                            {
                                // Show day with no shifts
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5)
                                    .Text($"{day.DayName}\n{day.Date:MM/dd}").FontSize(9);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5)
                                    .Text("No shifts scheduled").Italic();
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5);
                            }
                            else
                            {
                                foreach (var shift in day.Shifts)
                                {
                                    // Date column (only show on first shift of day)
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5)
                                        .Text(isFirstShiftForDay ? $"{day.DayName}\n{day.Date:MM/dd}" : "")
                                        .FontSize(9);

                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5)
                                        .Text(shift.ShiftName);

                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5)
                                        .Text(shift.TimeRange).FontSize(9);

                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5)
                                        .Text($"{shift.AssignedStaff}/{shift.RequiredStaff}")
                                        .FontColor(shift.AssignedStaff < shift.RequiredStaff ? Colors.Red.Medium : Colors.Black);

                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(5)
                                        .Text(shift.AssignedEmployees.Count > 0
                                            ? string.Join(", ", shift.AssignedEmployees)
                                            : "-")
                                        .FontSize(9);

                                    isFirstShiftForDay = false;
                                }
                            }

                            // Add chores and on-duty if present
                            if (day.Chores.Count > 0 || day.OnDuties.Count > 0)
                            {
                                foreach (var chore in day.Chores)
                                {
                                    table.Cell().Background(Colors.Blue.Lighten5).Padding(5).Text("");
                                    table.Cell().Background(Colors.Blue.Lighten5).Padding(5)
                                        .Text($"[Chore] {chore.ChoreName}").FontSize(9);
                                    table.Cell().Background(Colors.Blue.Lighten5).Padding(5).Text("");
                                    table.Cell().Background(Colors.Blue.Lighten5).Padding(5).Text("");
                                    table.Cell().Background(Colors.Blue.Lighten5).Padding(5)
                                        .Text(chore.AssignedTo ?? "-").FontSize(9);
                                }

                                foreach (var onDuty in day.OnDuties)
                                {
                                    table.Cell().Background(Colors.Green.Lighten5).Padding(5).Text("");
                                    table.Cell().Background(Colors.Green.Lighten5).Padding(5)
                                        .Text($"[On-Duty] {onDuty.DutyName}").FontSize(9);
                                    table.Cell().Background(Colors.Green.Lighten5).Padding(5).Text("");
                                    table.Cell().Background(Colors.Green.Lighten5).Padding(5).Text("");
                                    table.Cell().Background(Colors.Green.Lighten5).Padding(5)
                                        .Text(onDuty.AssignedTo ?? "-").FontSize(9);
                                }
                            }
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span($"Generated: {data.GeneratedAt:yyyy-MM-dd HH:mm} UTC | Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
            });
        });

        var pdfBytes = document.GeneratePdf();
        return Task.FromResult(pdfBytes);
    }

    /// <inheritdoc />
    public Task<byte[]> GenerateExcelAsync(ScheduleExportData data)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Schedule");

        // Title and metadata
        worksheet.Cell("A1").Value = data.CompanyName;
        worksheet.Cell("A1").Style.Font.Bold = true;
        worksheet.Cell("A1").Style.Font.FontSize = 16;

        worksheet.Cell("A2").Value = $"Schedule Export: {data.StartDate:yyyy-MM-dd} to {data.EndDate:yyyy-MM-dd}";

        var filterInfo = new List<string>();
        if (!string.IsNullOrEmpty(data.DepartmentName))
            filterInfo.Add($"Department: {data.DepartmentName}");
        if (!string.IsNullOrEmpty(data.JobTypeName))
            filterInfo.Add($"Job Type: {data.JobTypeName}");

        if (filterInfo.Count > 0)
        {
            worksheet.Cell("A3").Value = string.Join(" | ", filterInfo);
        }

        // Headers (row 5)
        var headerRow = 5;
        worksheet.Cell(headerRow, 1).Value = "Date";
        worksheet.Cell(headerRow, 2).Value = "Day";
        worksheet.Cell(headerRow, 3).Value = "Shift";
        worksheet.Cell(headerRow, 4).Value = "Time";
        worksheet.Cell(headerRow, 5).Value = "Required";
        worksheet.Cell(headerRow, 6).Value = "Assigned";
        worksheet.Cell(headerRow, 7).Value = "Employees";
        worksheet.Cell(headerRow, 8).Value = "Type";

        // Style headers
        var headerRange = worksheet.Range(headerRow, 1, headerRow, 8);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        // Data rows
        var currentRow = headerRow + 1;

        foreach (var day in data.Days)
        {
            var isFirstRowForDay = true;

            if (day.Shifts.Count == 0)
            {
                // Show day with no shifts
                worksheet.Cell(currentRow, 1).Value = day.Date.ToString("yyyy-MM-dd");
                worksheet.Cell(currentRow, 2).Value = day.DayName;
                worksheet.Cell(currentRow, 3).Value = "No shifts scheduled";
                worksheet.Cell(currentRow, 8).Value = "Shift";
                currentRow++;
            }
            else
            {
                foreach (var shift in day.Shifts)
                {
                    worksheet.Cell(currentRow, 1).Value = isFirstRowForDay ? day.Date.ToString("yyyy-MM-dd") : "";
                    worksheet.Cell(currentRow, 2).Value = isFirstRowForDay ? day.DayName : "";
                    worksheet.Cell(currentRow, 3).Value = shift.ShiftName;
                    worksheet.Cell(currentRow, 4).Value = shift.TimeRange;
                    worksheet.Cell(currentRow, 5).Value = shift.RequiredStaff;
                    worksheet.Cell(currentRow, 6).Value = shift.AssignedStaff;
                    worksheet.Cell(currentRow, 7).Value = string.Join(", ", shift.AssignedEmployees);
                    worksheet.Cell(currentRow, 8).Value = "Shift";

                    // Highlight understaffed shifts
                    if (shift.AssignedStaff < shift.RequiredStaff)
                    {
                        worksheet.Cell(currentRow, 6).Style.Font.FontColor = XLColor.Red;
                    }

                    isFirstRowForDay = false;
                    currentRow++;
                }
            }

            // Add chores
            foreach (var chore in day.Chores)
            {
                worksheet.Cell(currentRow, 1).Value = "";
                worksheet.Cell(currentRow, 2).Value = "";
                worksheet.Cell(currentRow, 3).Value = chore.ChoreName;
                worksheet.Cell(currentRow, 7).Value = chore.AssignedTo ?? "";
                worksheet.Cell(currentRow, 8).Value = "Chore";

                worksheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.LightBlue;
                currentRow++;
            }

            // Add on-duty
            foreach (var onDuty in day.OnDuties)
            {
                worksheet.Cell(currentRow, 1).Value = "";
                worksheet.Cell(currentRow, 2).Value = "";
                worksheet.Cell(currentRow, 3).Value = onDuty.DutyName;
                worksheet.Cell(currentRow, 7).Value = onDuty.AssignedTo ?? "";
                worksheet.Cell(currentRow, 8).Value = "On-Duty";

                worksheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.LightGreen;
                currentRow++;
            }
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        // Add footer
        worksheet.Cell(currentRow + 2, 1).Value = $"Generated: {data.GeneratedAt:yyyy-MM-dd HH:mm} UTC";
        worksheet.Cell(currentRow + 2, 1).Style.Font.Italic = true;

        // Save to memory stream
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Task.FromResult(stream.ToArray());
    }

    /// <inheritdoc />
    public Task<byte[]> GenerateCsvAsync(ScheduleExportData data)
    {
        var sb = new StringBuilder();

        // CSV Header
        sb.AppendLine("Date,Day,Shift,Time,Required,Assigned,Employees,Type");

        foreach (var day in data.Days)
        {
            var isFirstRowForDay = true;

            if (day.Shifts.Count == 0)
            {
                // Show day with no shifts
                sb.AppendLine($"{day.Date:yyyy-MM-dd},{day.DayName},\"No shifts scheduled\",,,,,Shift");
            }
            else
            {
                foreach (var shift in day.Shifts)
                {
                    var date = isFirstRowForDay ? day.Date.ToString("yyyy-MM-dd") : "";
                    var dayName = isFirstRowForDay ? day.DayName : "";
                    var employees = EscapeCsvField(string.Join("; ", shift.AssignedEmployees));

                    sb.AppendLine($"{date},{dayName},{EscapeCsvField(shift.ShiftName)},{shift.TimeRange},{shift.RequiredStaff},{shift.AssignedStaff},{employees},Shift");
                    isFirstRowForDay = false;
                }
            }

            // Add chores
            foreach (var chore in day.Chores)
            {
                sb.AppendLine($",,{EscapeCsvField(chore.ChoreName)},,,,{EscapeCsvField(chore.AssignedTo ?? "")},Chore");
            }

            // Add on-duty
            foreach (var onDuty in day.OnDuties)
            {
                sb.AppendLine($",,{EscapeCsvField(onDuty.DutyName)},,,,{EscapeCsvField(onDuty.AssignedTo ?? "")},On-Duty");
            }
        }

        // Add metadata comment at end
        sb.AppendLine();
        sb.AppendLine($"# Generated: {data.GeneratedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"# Company: {data.CompanyName}");
        sb.AppendLine($"# Period: {data.StartDate:yyyy-MM-dd} to {data.EndDate:yyyy-MM-dd}");

        // Return UTF-8 encoded bytes with BOM for Excel compatibility
        var preamble = Encoding.UTF8.GetPreamble();
        var csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + csvBytes.Length];
        preamble.CopyTo(result, 0);
        csvBytes.CopyTo(result, preamble.Length);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Formats a time range for display.
    /// </summary>
    private static string FormatTimeRange(TimeOnly start, TimeOnly end)
    {
        var startStr = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        var endStr = end.ToString("HH:mm", CultureInfo.InvariantCulture);
        return $"{startStr}-{endStr}";
    }

    /// <summary>
    /// Formats an OnDutyType enum value for display.
    /// </summary>
    private static string FormatOnDutyType(OnDutyType type)
    {
        return type switch
        {
            OnDutyType.Hakam => "Hakam",
            OnDutyType.Lead => "Lead",
            _ => type.ToString()
        };
    }

    /// <summary>
    /// Escapes a field for CSV output.
    /// </summary>
    private static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // If the value contains quotes, commas, or newlines, wrap in quotes and escape quotes
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
