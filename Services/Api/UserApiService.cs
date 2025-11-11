using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Models.Support;

namespace ShiftManager.Services.Api;

/// <summary>
/// Wrapper service for User API operations.
/// Isolates API logic from existing UserService to maintain zero regression.
/// </summary>
public class UserApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserApiService> _logger;

    public UserApiService(AppDbContext context, ILogger<UserApiService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lists users with pagination and filtering.
    /// Respects global query filters for multi-tenant isolation.
    /// </summary>
    public async Task<(List<UserDto> Users, int TotalCount)> ListUsersAsync(
        int companyId,
        int page = 1,
        int pageSize = 50,
        string? role = null,
        bool? isActive = null,
        string? search = null)
    {
        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100; // Max page size

        var query = _context.Users.AsQueryable();

        // Manual CompanyId filter (even though global filter applies, explicit is safer for API)
        query = query.Where(u => u.CompanyId == companyId);

        // Apply role filter
        if (!string.IsNullOrEmpty(role))
        {
            if (Enum.TryParse<UserRole>(role, true, out var roleEnum))
            {
                query = query.Where(u => u.Role == roleEnum);
            }
        }

        // Apply active status filter
        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        // Apply search filter (email or display name)
        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(searchLower) ||
                u.DisplayName.ToLower().Contains(searchLower));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply pagination
        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs (basic details only for list endpoint)
        var dtos = users.Select(u => UserDto.FromEntity(u, includeFullDetails: false)).ToList();

        return (dtos, totalCount);
    }

    /// <summary>
    /// Gets a single user by ID with full details.
    /// Respects global query filters for multi-tenant isolation.
    /// </summary>
    public async Task<UserDto?> GetUserAsync(int companyId, int userId)
    {
        var user = await _context.Users
            .Where(u => u.CompanyId == companyId && u.Id == userId)
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return null;
        }

        // Return full details for single user endpoint
        return UserDto.FromEntity(user, includeFullDetails: true);
    }

    /// <summary>
    /// Creates a new user via API.
    /// Returns (user, error) tuple - if error is not null, user will be null.
    /// </summary>
    public async Task<(UserDto? User, string? Error)> CreateUserAsync(
        int companyId,
        string email,
        string displayName,
        string role,
        string? password = null,
        string? department = null,
        string? jobTitle = null)
    {
        // Validate email uniqueness
        var existingUser = await _context.Users
            .IgnoreQueryFilters() // Check across all companies
            .AnyAsync(u => u.Email == email);

        if (existingUser)
        {
            return (null, $"A user with email '{email}' already exists");
        }

        // Validate role
        if (!Enum.TryParse<UserRole>(role, true, out var roleEnum))
        {
            var validRoles = string.Join(", ", Enum.GetNames<UserRole>());
            return (null, $"Invalid role '{role}'. Valid roles: {validRoles}");
        }

        // Create user
        var user = new AppUser
        {
            CompanyId = companyId,
            Email = email,
            DisplayName = displayName,
            Role = roleEnum,
            IsActive = true,
            Department = department,
            JobTitle = jobTitle
        };

        // Set password if provided (otherwise, user must reset)
        if (!string.IsNullOrEmpty(password))
        {
            // Generate salt and hash
            using var hmac = new System.Security.Cryptography.HMACSHA512();
            user.PasswordSalt = hmac.Key;
            user.PasswordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        }
        else
        {
            // Generate random salt for now (user must reset password)
            using var hmac = new System.Security.Cryptography.HMACSHA512();
            user.PasswordSalt = hmac.Key;
            user.PasswordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User created via API: {Email} (CompanyId: {CompanyId}, UserId: {UserId})",
            email, companyId, user.Id);

        return (UserDto.FromEntity(user, includeFullDetails: true), null);
    }

    /// <summary>
    /// Updates an existing user via API.
    /// Only updates fields that are provided (partial update).
    /// Returns (user, error) tuple.
    /// </summary>
    public async Task<(UserDto? User, string? Error)> UpdateUserAsync(
        int companyId,
        int userId,
        string? displayName = null,
        string? role = null,
        bool? isActive = null,
        string? department = null,
        string? jobTitle = null,
        string? phone = null)
    {
        var user = await _context.Users
            .Where(u => u.CompanyId == companyId && u.Id == userId)
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return (null, $"User {userId} not found in company {companyId}");
        }

        // Apply updates only for provided fields
        if (displayName != null)
        {
            user.DisplayName = displayName;
        }

        if (role != null)
        {
            if (!Enum.TryParse<UserRole>(role, true, out var roleEnum))
            {
                var validRoles = string.Join(", ", Enum.GetNames<UserRole>());
                return (null, $"Invalid role '{role}'. Valid roles: {validRoles}");
            }
            user.Role = roleEnum;
        }

        if (isActive.HasValue)
        {
            user.IsActive = isActive.Value;
        }

        if (department != null)
        {
            user.Department = department;
        }

        if (jobTitle != null)
        {
            user.JobTitle = jobTitle;
        }

        if (phone != null)
        {
            user.Phone = phone;
        }

        user.ProfileLastUpdated = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("User updated via API: {Email} (CompanyId: {CompanyId}, UserId: {UserId})",
            user.Email, companyId, userId);

        return (UserDto.FromEntity(user, includeFullDetails: true), null);
    }

    /// <summary>
    /// Validates that an API key has permission to access a specific company.
    /// Used by controllers to enforce authorization.
    /// </summary>
    public bool ValidateCompanyAccess(int apiKeyCompanyId, int requestedCompanyId)
    {
        return apiKeyCompanyId == requestedCompanyId;
    }
}
