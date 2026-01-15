using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsAdmin")]
public class DirectorsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<DirectorsModel> _logger;

    public DirectorsModel(AppDbContext db, ILogger<DirectorsModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public record DirectorAssignmentVM(int Id, string DirectorName, string DirectorEmail, int CompanyId, string CompanyName, string? CompanySlug, string GrantedByName, DateTime GrantedAt);

    public List<DirectorAssignmentVM> Assignments { get; set; } = new();
    public List<AppUser> AvailableDirectors { get; set; } = new();
    public List<Company> AvailableCompanies { get; set; } = new();

    [BindProperty] public int DirectorUserId { get; set; }
    [BindProperty] public int CompanyId { get; set; }

    public string? Error { get; set; }
    public string? Success { get; set; }

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg)
        {
            Success = successMsg;
        }

        if (TempData["ErrorMessage"] is string errorMsg)
        {
            Error = errorMsg;
        }

        // Load all director assignments - use AsNoTracking for better performance
        var directorCompanies = await _db.DirectorCompanies
            .AsNoTracking()
            .Where(dc => !dc.IsDeleted)
            .ToListAsync();

        // Load related entities
        var userIds = directorCompanies.SelectMany(dc => new[] { dc.UserId, dc.GrantedBy }).Distinct().ToList();
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var companyIds = directorCompanies.Select(dc => dc.CompanyId).Distinct().ToList();
        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        // Project to VM
        Assignments = directorCompanies
            .Select(dc => new DirectorAssignmentVM(
                dc.Id,
                users[dc.UserId].DisplayName,
                users[dc.UserId].Email,
                dc.CompanyId,
                companies[dc.CompanyId].Name,
                companies[dc.CompanyId].Slug,
                users[dc.GrantedBy].DisplayName,
                dc.GrantedAt
            ))
            .OrderBy(a => a.CompanyName)
            .ThenBy(a => a.DirectorName)
            .ToList();

        // Load all users with Director role
        AvailableDirectors = await _db.Users
            .Where(u => u.Role == UserRole.Director && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        // Load all companies (including soft-deleted for reassignment scenarios)
        AvailableCompanies = await _db.Companies
            .IgnoreQueryFilters()
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAssignAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = "Invalid user claim. Please log in again.";
            return RedirectToPage();
        }

        // Validate inputs
        if (DirectorUserId == 0 || CompanyId == 0)
        {
            TempData["ErrorMessage"] = "Please select both a director and a company.";
            return RedirectToPage();
        }

        // Check if director user exists and has Director role
        var director = await _db.Users.FindAsync(DirectorUserId);
        if (director == null || director.Role != UserRole.Director)
        {
            TempData["ErrorMessage"] = "Selected user is not a Director.";
            return RedirectToPage();
        }

        // Check if company exists
        var company = await _db.Companies.FindAsync(CompanyId);
        if (company == null)
        {
            TempData["ErrorMessage"] = "Company not found.";
            return RedirectToPage();
        }

        // Check if assignment already exists
        var existingAssignment = await _db.DirectorCompanies
            .FirstOrDefaultAsync(dc => dc.UserId == DirectorUserId && dc.CompanyId == CompanyId && !dc.IsDeleted);

        if (existingAssignment != null)
        {
            TempData["ErrorMessage"] = $"{director.DisplayName} is already assigned to {company.Name}.";
            return RedirectToPage();
        }

        // Create new assignment
        var newAssignment = new DirectorCompany
        {
            UserId = DirectorUserId,
            CompanyId = CompanyId,
            GrantedBy = currentUserId,
            GrantedAt = DateTime.UtcNow
        };

        _db.DirectorCompanies.Add(newAssignment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Assigned Director {DirectorEmail} to Company {CompanyName} by {GrantedBy}",
            director.Email, company.Name, currentUserId);

        TempData["SuccessMessage"] = $"Successfully assigned {director.DisplayName} as Director of {company.Name}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        var assignment = await _db.DirectorCompanies
            .Include(dc => dc.User)
            .Include(dc => dc.Company)
            .FirstOrDefaultAsync(dc => dc.Id == id && !dc.IsDeleted);

        if (assignment == null)
        {
            TempData["ErrorMessage"] = "Assignment not found.";
            return RedirectToPage();
        }

        // Soft delete
        assignment.IsDeleted = true;
        assignment.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Revoked Director access for {DirectorEmail} from Company {CompanyName}",
            assignment.User!.Email, assignment.Company!.Name);

        TempData["SuccessMessage"] = $"Revoked Director access for {assignment.User.DisplayName} from {assignment.Company.Name}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReassignAsync(int id, int newCompanyId)
    {
        var assignment = await _db.DirectorCompanies
            .FirstOrDefaultAsync(dc => dc.Id == id && !dc.IsDeleted);

        if (assignment == null)
        {
            TempData["ErrorMessage"] = "Assignment not found.";
            return RedirectToPage();
        }

        // Check if new company exists
        var company = await _db.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == newCompanyId);
        if (company == null)
        {
            TempData["ErrorMessage"] = "Company not found.";
            return RedirectToPage();
        }

        // Check for duplicate
        var existingAssignment = await _db.DirectorCompanies
            .FirstOrDefaultAsync(dc => dc.UserId == assignment.UserId &&
                                       dc.CompanyId == newCompanyId &&
                                       !dc.IsDeleted);
        if (existingAssignment != null)
        {
            TempData["ErrorMessage"] = "Director is already assigned to that company.";
            return RedirectToPage();
        }

        // Soft delete old, create new
        assignment.IsDeleted = true;
        assignment.DeletedAt = DateTime.UtcNow;

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);

        var newAssignment = new DirectorCompany
        {
            UserId = assignment.UserId,
            CompanyId = newCompanyId,
            GrantedBy = currentUserId,
            GrantedAt = DateTime.UtcNow
        };

        _db.DirectorCompanies.Add(newAssignment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Reassigned Director {UserId} from Company {OldCompanyId} to {NewCompanyId}",
            assignment.UserId, assignment.CompanyId, newCompanyId);

        TempData["SuccessMessage"] = $"Director reassigned to {company.Name}.";
        return RedirectToPage();
    }
}
