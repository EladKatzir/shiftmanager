using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for avatar upload and management
/// </summary>
public interface IAvatarService
{
    Task<(bool Success, string? FileName, string? Error)> UploadAvatarAsync(int userId, IFormFile file);
    Task<bool> DeleteAvatarAsync(int userId);
    string GetAvatarUrl(int userId, string? avatarFileName, bool thumbnail = false);
    string GetDefaultAvatarInitials(string displayName);
}

/// <summary>
/// Service for managing user avatar uploads with image processing
/// </summary>
public class AvatarService : IAvatarService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AvatarService> _logger;

    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB
    private const int FullSize = 400;
    private const int ThumbnailSize = 100;
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };

    public AvatarService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IWebHostEnvironment env,
        ILogger<AvatarService> logger)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _env = env;
        _logger = logger;
    }

    public async Task<(bool Success, string? FileName, string? Error)> UploadAvatarAsync(int userId, IFormFile file)
    {
        try
        {
            // Validate file
            if (file == null || file.Length == 0)
            {
                return (false, null, "No file uploaded");
            }

            if (file.Length > MaxFileSize)
            {
                return (false, null, $"File size exceeds {MaxFileSize / 1024 / 1024} MB limit");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                return (false, null, "Only JPEG and PNG files are allowed");
            }

            // SECURITY ENHANCEMENT: Validate actual file content-type from headers
            var contentType = file.ContentType.ToLowerInvariant();
            if (contentType != "image/jpeg" && contentType != "image/jpg" && contentType != "image/png")
            {
                _logger.LogWarning("Avatar upload rejected: Invalid content type {ContentType} for user {UserId}", contentType, userId);
                return (false, null, "Invalid image format");
            }

            // Get user and company info
            var user = await _db.Users.FindAsync(userId);
            if (user == null)
            {
                return (false, null, "User not found");
            }

            var companyId = _tenantResolver.GetCurrentTenantId();

            // Ensure avatars directory exists
            var avatarsDir = Path.Combine(_env.WebRootPath, "avatars", companyId.ToString());
            if (!Directory.Exists(avatarsDir))
            {
                Directory.CreateDirectory(avatarsDir);
            }

            // Delete old avatar if exists
            if (!string.IsNullOrWhiteSpace(user.AvatarFileName))
            {
                await DeleteAvatarFilesAsync(companyId, user.AvatarFileName);
            }

            // Generate new filename
            var fileName = $"{userId}.jpg";
            var fullPath = Path.Combine(avatarsDir, fileName);
            var thumbPath = Path.Combine(avatarsDir, $"{userId}_thumb.jpg");

            // Process and save image
            // SECURITY FIX: ImageSharp.LoadAsync validates file format and detects malicious files
            using (var stream = file.OpenReadStream())
            {
                Image image;
                try
                {
                    image = await Image.LoadAsync(stream);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Invalid or corrupted image file uploaded by user {UserId}", userId);
                    return (false, null, "Invalid or corrupted image file");
                }

                using (image)
                {
                    // Strip EXIF metadata (may contain GPS coordinates — OPSEC concern)
                    image.Metadata.ExifProfile = null;
                    image.Metadata.IccProfile = null;
                    image.Metadata.IptcProfile = null;
                    image.Metadata.XmpProfile = null;

                    // Resize to 400x400 (crop to center if not square)
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(FullSize, FullSize),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    }));

                    // Save full size avatar
                    await image.SaveAsJpegAsync(fullPath, new JpegEncoder { Quality = 85 });

                    // Create and save thumbnail (100x100)
                    image.Mutate(x => x.Resize(new Size(ThumbnailSize, ThumbnailSize)));
                    await image.SaveAsJpegAsync(thumbPath, new JpegEncoder { Quality = 85 });
                }
            }

            // Update database
            user.AvatarFileName = fileName;
            user.ProfileLastUpdated = DateTime.UtcNow;
            // ProfileLastUpdatedBy will be set by the calling service
            await _db.SaveChangesAsync();

            _logger.LogInformation("Avatar uploaded successfully for user {UserId}", userId);
            return (true, fileName, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading avatar for user {UserId}", userId);
            return (false, null, "Error processing image. Please try again.");
        }
    }

    public async Task<bool> DeleteAvatarAsync(int userId)
    {
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null || string.IsNullOrWhiteSpace(user.AvatarFileName))
            {
                return false;
            }

            var companyId = _tenantResolver.GetCurrentTenantId();

            // Delete files
            await DeleteAvatarFilesAsync(companyId, user.AvatarFileName);

            // Update database
            user.AvatarFileName = null;
            user.ProfileLastUpdated = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Avatar deleted for user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting avatar for user {UserId}", userId);
            return false;
        }
    }

    public string GetAvatarUrl(int userId, string? avatarFileName, bool thumbnail = false)
    {
        if (string.IsNullOrWhiteSpace(avatarFileName))
        {
            // Return empty string for CSS-based default avatar
            return string.Empty;
        }

        var companyId = _tenantResolver.GetCurrentTenantId();
        var fileName = thumbnail ? $"{userId}_thumb.jpg" : avatarFileName;
        return $"/avatars/{companyId}/{fileName}";
    }

    public string GetDefaultAvatarInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        var parts = displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            // First and last name initials
            return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
        }
        else if (parts.Length == 1 && parts[0].Length >= 2)
        {
            // First two letters
            return parts[0].Substring(0, 2).ToUpper();
        }
        else if (parts.Length == 1 && parts[0].Length == 1)
        {
            // Single letter
            return parts[0].ToUpper();
        }

        return "?";
    }

    private async Task DeleteAvatarFilesAsync(int companyId, string fileName)
    {
        try
        {
            var avatarsDir = Path.Combine(_env.WebRootPath, "avatars", companyId.ToString());

            // Extract user ID from filename (e.g., "123.jpg" -> "123")
            var userIdStr = Path.GetFileNameWithoutExtension(fileName);

            // Delete full size
            var fullPath = Path.Combine(avatarsDir, fileName);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            // Delete thumbnail
            var thumbPath = Path.Combine(avatarsDir, $"{userIdStr}_thumb.jpg");
            if (File.Exists(thumbPath))
            {
                File.Delete(thumbPath);
            }

            await Task.CompletedTask; // Make method async for consistency
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting avatar files for company {CompanyId}, file {FileName}", companyId, fileName);
            // Don't throw - file deletion failure shouldn't break the operation
        }
    }
}
