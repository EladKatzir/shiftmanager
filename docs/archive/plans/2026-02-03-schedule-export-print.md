# Schedule Export & Print Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable users to export weekly/monthly schedules as PDF, Excel, or CSV files, and provide print-optimized views for physical printouts.

**Architecture:** Extend existing ArchiveService pattern. Add ScheduleExportService with format-specific generators. Use QuestPDF for PDF generation (air-gapped compatible, no external dependencies). Add export buttons to Calendar pages with format selector dropdown.

**Tech Stack:** ASP.NET Core 8, QuestPDF (NuGet), ClosedXML (NuGet for Excel), existing CSV pattern from ArchiveService

---

## Task 1: Add NuGet Dependencies

**Files:**
- Modify: `ShiftManager.csproj`

**Step 1: Add package references**

```xml
<PackageReference Include="QuestPDF" Version="2024.3.0" />
<PackageReference Include="ClosedXML" Version="0.102.2" />
```

Note: QuestPDF is fully self-contained, no external services needed (air-gapped compatible).

**Step 2: Restore packages**

Run:
```bash
dotnet restore ShiftManager.sln
```

**Step 3: Commit**

```bash
git add ShiftManager.csproj
git commit -m "chore: add QuestPDF and ClosedXML for schedule export"
```

---

## Task 2: Create Export DTOs

**Files:**
- Create: `Models/Export/ScheduleExportRequest.cs`
- Create: `Models/Export/ScheduleExportData.cs`

**Step 1: Create request DTO**

```csharp
namespace ShiftManager.Models.Export;

public enum ExportFormat
{
    Pdf = 0,
    Excel = 1,
    Csv = 2
}

public enum ExportPeriod
{
    Week = 0,
    Month = 1,
    Custom = 2
}

public class ScheduleExportRequest
{
    public ExportFormat Format { get; set; } = ExportFormat.Pdf;
    public ExportPeriod Period { get; set; } = ExportPeriod.Week;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Optional: Filter by specific department
    /// </summary>
    public int? DepartmentId { get; set; }

    /// <summary>
    /// Optional: Filter by specific job type
    /// </summary>
    public int? JobTypeId { get; set; }

    /// <summary>
    /// Include employee names or just counts
    /// </summary>
    public bool IncludeEmployeeNames { get; set; } = true;

    /// <summary>
    /// Include chores in export
    /// </summary>
    public bool IncludeChores { get; set; } = true;

    /// <summary>
    /// Include on-duty assignments
    /// </summary>
    public bool IncludeOnDuty { get; set; } = true;
}
```

**Step 2: Create data DTO**

```csharp
namespace ShiftManager.Models.Export;

public class ScheduleExportData
{
    public string CompanyName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? DepartmentName { get; set; }
    public string? JobTypeName { get; set; }

    public List<ExportDayData> Days { get; set; } = new();
}

public class ExportDayData
{
    public DateTime Date { get; set; }
    public string DayName { get; set; } = string.Empty;
    public List<ExportShiftData> Shifts { get; set; } = new();
    public List<ExportChoreData> Chores { get; set; } = new();
    public List<ExportOnDutyData> OnDuties { get; set; } = new();
}

public class ExportShiftData
{
    public string ShiftName { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public int RequiredStaff { get; set; }
    public int AssignedStaff { get; set; }
    public List<string> AssignedEmployees { get; set; } = new();
}

public class ExportChoreData
{
    public string ChoreName { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public bool IsCompleted { get; set; }
}

public class ExportOnDutyData
{
    public string DutyName { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
}
```

**Step 3: Commit**

```bash
git add Models/Export/
git commit -m "feat: add schedule export DTOs"
```

---

## Task 3: Create Schedule Export Service

**Files:**
- Create: `Services/IScheduleExportService.cs`
- Create: `Services/ScheduleExportService.cs`

**Step 1: Create the interface**

```csharp
using ShiftManager.Models.Export;

namespace ShiftManager.Services;

public interface IScheduleExportService
{
    Task<ScheduleExportData> CollectExportDataAsync(ScheduleExportRequest request);
    Task<byte[]> GeneratePdfAsync(ScheduleExportData data);
    Task<byte[]> GenerateExcelAsync(ScheduleExportData data);
    Task<byte[]> GenerateCsvAsync(ScheduleExportData data);
}
```

**Step 2: Create the service implementation**

```csharp
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Export;
using System.Text;

namespace ShiftManager.Services;

public class ScheduleExportService : IScheduleExportService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly ICompanyService _companyService;

    public ScheduleExportService(
        ApplicationDbContext db,
        ITenantResolver tenantResolver,
        ICompanyService companyService)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _companyService = companyService;

        // Configure QuestPDF license (Community license for open source)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<ScheduleExportData> CollectExportDataAsync(ScheduleExportRequest request)
    {
        var company = await _companyService.GetCurrentCompanyAsync();

        var data = new ScheduleExportData
        {
            CompanyName = company?.Name ?? "Schedule",
            GeneratedAt = DateTime.UtcNow,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };

        // Get department/job type names if filtered
        if (request.DepartmentId.HasValue)
        {
            var dept = await _db.Departments.FindAsync(request.DepartmentId.Value);
            data.DepartmentName = dept?.Name;
        }

        if (request.JobTypeId.HasValue)
        {
            var jt = await _db.JobTypes.FindAsync(request.JobTypeId.Value);
            data.JobTypeName = jt?.Name;
        }

        // Collect data for each day
        for (var date = request.StartDate; date <= request.EndDate; date = date.AddDays(1))
        {
            var dayData = new ExportDayData
            {
                Date = date,
                DayName = date.ToString("dddd")
            };

            // Get shifts for the day
            var shifts = await _db.ShiftInstances
                .Include(si => si.Shift)
                .Include(si => si.Assignments)
                    .ThenInclude(a => a.Employee)
                .Where(si => si.WorkDate == DateOnly.FromDateTime(date))
                .ToListAsync();

            foreach (var shift in shifts)
            {
                var shiftData = new ExportShiftData
                {
                    ShiftName = shift.Shift?.Name ?? "Unknown",
                    TimeRange = $"{shift.StartTime:HH:mm} - {shift.EndTime:HH:mm}",
                    RequiredStaff = shift.RequiredEmployees,
                    AssignedStaff = shift.Assignments.Count
                };

                if (request.IncludeEmployeeNames)
                {
                    shiftData.AssignedEmployees = shift.Assignments
                        .Select(a => a.Employee?.DisplayName ?? "Unknown")
                        .ToList();
                }

                dayData.Shifts.Add(shiftData);
            }

            // Get chores if requested
            if (request.IncludeChores)
            {
                var chores = await _db.ChoreInstances
                    .Include(c => c.ChoreType)
                    .Include(c => c.AssignedUser)
                    .Where(c => c.DueDate == DateOnly.FromDateTime(date))
                    .ToListAsync();

                dayData.Chores = chores.Select(c => new ExportChoreData
                {
                    ChoreName = c.ChoreType?.Name ?? "Unknown",
                    AssignedTo = request.IncludeEmployeeNames ? c.AssignedUser?.DisplayName : null,
                    IsCompleted = c.IsCompleted
                }).ToList();
            }

            // Get on-duty if requested
            if (request.IncludeOnDuty)
            {
                var onDuties = await _db.OnDutyInstances
                    .Include(o => o.OnDutyType)
                    .Include(o => o.AssignedUser)
                    .Where(o => o.Date == DateOnly.FromDateTime(date))
                    .ToListAsync();

                dayData.OnDuties = onDuties.Select(o => new ExportOnDutyData
                {
                    DutyName = o.OnDutyType?.Name ?? "Unknown",
                    AssignedTo = request.IncludeEmployeeNames ? o.AssignedUser?.DisplayName : null
                }).ToList();
            }

            data.Days.Add(dayData);
        }

        return data;
    }

    public async Task<byte[]> GeneratePdfAsync(ScheduleExportData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header()
                    .Text(text =>
                    {
                        text.Span($"{data.CompanyName} - Schedule").Bold().FontSize(16);
                        text.EmptyLine();
                        text.Span($"{data.StartDate:MMM d} - {data.EndDate:MMM d, yyyy}");
                        if (!string.IsNullOrEmpty(data.DepartmentName))
                            text.Span($" | {data.DepartmentName}");
                    });

                page.Content()
                    .Table(table =>
                    {
                        // Define columns: Day, Date, Shifts, Assigned
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(80);  // Day name
                            columns.ConstantColumn(70);  // Date
                            columns.RelativeColumn(2);   // Shifts
                            columns.RelativeColumn(3);   // Assigned employees
                        });

                        // Header row
                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Day").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Date").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Shifts").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Assigned").Bold();
                        });

                        // Data rows
                        foreach (var day in data.Days)
                        {
                            table.Cell().BorderBottom(1).Padding(5).Text(day.DayName);
                            table.Cell().BorderBottom(1).Padding(5).Text(day.Date.ToString("MMM d"));

                            table.Cell().BorderBottom(1).Padding(5).Column(col =>
                            {
                                foreach (var shift in day.Shifts)
                                {
                                    col.Item().Text($"{shift.ShiftName} ({shift.TimeRange})");
                                }
                            });

                            table.Cell().BorderBottom(1).Padding(5).Column(col =>
                            {
                                foreach (var shift in day.Shifts)
                                {
                                    var names = string.Join(", ", shift.AssignedEmployees);
                                    col.Item().Text($"{shift.AssignedStaff}/{shift.RequiredStaff}: {names}");
                                }
                            });
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span($"Generated: {data.GeneratedAt:yyyy-MM-dd HH:mm} | Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
            });
        });

        return await Task.FromResult(document.GeneratePdf());
    }

    public async Task<byte[]> GenerateExcelAsync(ScheduleExportData data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Schedule");

        // Header
        ws.Cell(1, 1).Value = $"{data.CompanyName} - Schedule";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"{data.StartDate:MMM d} - {data.EndDate:MMM d, yyyy}";

        // Column headers
        var headerRow = 4;
        ws.Cell(headerRow, 1).Value = "Day";
        ws.Cell(headerRow, 2).Value = "Date";
        ws.Cell(headerRow, 3).Value = "Shift";
        ws.Cell(headerRow, 4).Value = "Time";
        ws.Cell(headerRow, 5).Value = "Required";
        ws.Cell(headerRow, 6).Value = "Assigned";
        ws.Cell(headerRow, 7).Value = "Employees";

        var range = ws.Range(headerRow, 1, headerRow, 7);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;

        // Data rows
        var row = headerRow + 1;
        foreach (var day in data.Days)
        {
            foreach (var shift in day.Shifts)
            {
                ws.Cell(row, 1).Value = day.DayName;
                ws.Cell(row, 2).Value = day.Date;
                ws.Cell(row, 3).Value = shift.ShiftName;
                ws.Cell(row, 4).Value = shift.TimeRange;
                ws.Cell(row, 5).Value = shift.RequiredStaff;
                ws.Cell(row, 6).Value = shift.AssignedStaff;
                ws.Cell(row, 7).Value = string.Join(", ", shift.AssignedEmployees);
                row++;
            }
        }

        // Auto-fit columns
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return await Task.FromResult(stream.ToArray());
    }

    public async Task<byte[]> GenerateCsvAsync(ScheduleExportData data)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("Day,Date,Shift,Time,Required,Assigned,Employees");

        // Data rows
        foreach (var day in data.Days)
        {
            foreach (var shift in day.Shifts)
            {
                var employees = EscapeCsv(string.Join("; ", shift.AssignedEmployees));
                sb.AppendLine($"{day.DayName},{day.Date:yyyy-MM-dd},{EscapeCsv(shift.ShiftName)},{shift.TimeRange},{shift.RequiredStaff},{shift.AssignedStaff},{employees}");
            }
        }

        return await Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
```

**Step 3: Register service in DI**

In `Program.cs`, add:
```csharp
builder.Services.AddScoped<IScheduleExportService, ScheduleExportService>();
```

**Step 4: Commit**

```bash
git add Services/IScheduleExportService.cs Services/ScheduleExportService.cs Program.cs
git commit -m "feat: add ScheduleExportService with PDF, Excel, CSV generation"
```

---

## Task 4: Create Export API Endpoint

**Files:**
- Create: `Pages/Api/ScheduleExport.cshtml.cs`

**Step 1: Create the API endpoint**

```csharp
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
```

**Step 2: Add to API middleware whitelist**

In `Middleware/ApiAuthenticationMiddleware.cs`, add `/Api/ScheduleExport` to `IsInternalWebUiEndpoint()`.

**Step 3: Commit**

```bash
git add Pages/Api/ScheduleExport.cshtml.cs Middleware/ApiAuthenticationMiddleware.cs
git commit -m "feat: add ScheduleExport API endpoint"
```

---

## Task 5: Add Export Button to Calendar Page

**Files:**
- Modify: `Pages/Calendar/Month.cshtml`
- Modify: `Pages/Calendar/Month.cshtml.cs`

**Step 1: Add export dropdown button to calendar header**

In the page header section, add:

```html
<div class="export-dropdown">
    <button type="button" class="btn btn-secondary" id="exportBtn">
        <i data-lucide="download"></i>
        <loc key="Export" />
    </button>
    <div class="dropdown-menu" id="exportMenu">
        <button type="button" class="dropdown-item" data-format="pdf">
            <i data-lucide="file-text"></i> PDF
        </button>
        <button type="button" class="dropdown-item" data-format="excel">
            <i data-lucide="file-spreadsheet"></i> Excel
        </button>
        <button type="button" class="dropdown-item" data-format="csv">
            <i data-lucide="file-text"></i> CSV
        </button>
        <div class="dropdown-divider"></div>
        <button type="button" class="dropdown-item" onclick="window.print()">
            <i data-lucide="printer"></i> <loc key="Print" />
        </button>
    </div>
</div>
```

**Step 2: Add JavaScript for export**

```javascript
document.querySelectorAll('[data-format]').forEach(btn => {
    btn.addEventListener('click', async function() {
        const format = this.dataset.format;
        const formatMap = { pdf: 0, excel: 1, csv: 2 };

        const request = {
            format: formatMap[format],
            period: 1, // Month
            startDate: '@Model.StartDate.ToString("yyyy-MM-dd")',
            endDate: '@Model.EndDate.ToString("yyyy-MM-dd")',
            includeEmployeeNames: true,
            includeChores: true,
            includeOnDuty: true
        };

        try {
            const response = await fetch('/Api/ScheduleExport', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(request)
            });

            if (response.ok) {
                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = response.headers.get('Content-Disposition')?.split('filename=')[1] || `schedule.${format}`;
                a.click();
                URL.revokeObjectURL(url);
            }
        } catch (error) {
            console.error('Export failed:', error);
        }
    });
});
```

**Step 3: Add print stylesheet**

Create `wwwroot/css/print.css`:

```css
@media print {
    /* Hide non-printable elements */
    .sidebar,
    .navbar,
    .export-dropdown,
    .btn,
    footer,
    .no-print {
        display: none !important;
    }

    /* Reset colors for print */
    body {
        background: white !important;
        color: black !important;
    }

    .calendar-grid {
        border: 1px solid #ccc;
    }

    .calendar-day {
        border: 1px solid #eee;
        page-break-inside: avoid;
    }

    /* Ensure good contrast */
    .shift-badge {
        border: 1px solid #999;
        background: white !important;
        color: black !important;
    }
}
```

**Step 4: Link print stylesheet in layout**

In `_Layout.cshtml`:
```html
<link rel="stylesheet" href="~/css/print.css" media="print">
```

**Step 5: Commit**

```bash
git add Pages/Calendar/Month.cshtml wwwroot/css/print.css Pages/Shared/_Layout.cshtml
git commit -m "feat: add export button and print stylesheet to calendar"
```

---

## Task 6: Add Unit Tests

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/ScheduleExportServiceTests.cs`

**Step 1: Write tests**

```csharp
using ShiftManager.Models.Export;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

public class ScheduleExportServiceTests
{
    [Fact]
    public async Task GeneratePdfAsync_ReturnsValidPdfBytes()
    {
        // Test PDF generation returns non-empty byte array
    }

    [Fact]
    public async Task GenerateExcelAsync_ReturnsValidXlsxBytes()
    {
        // Test Excel generation
    }

    [Fact]
    public async Task GenerateCsvAsync_ReturnsValidCsvContent()
    {
        // Test CSV generation with proper escaping
    }
}
```

**Step 2: Run tests**

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ScheduleExportService"
```

**Step 3: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/ScheduleExportServiceTests.cs
git commit -m "test: add ScheduleExportService unit tests"
```

---

## Verification Checklist

- [ ] QuestPDF and ClosedXML packages installed
- [ ] PDF export generates valid PDF file
- [ ] Excel export generates valid .xlsx file
- [ ] CSV export generates valid CSV with proper escaping
- [ ] Export button visible on calendar page
- [ ] Print stylesheet hides non-essential elements
- [ ] Ctrl+P opens print-friendly view
- [ ] Unit tests pass

---

## Estimated Effort: 24-32 hours

