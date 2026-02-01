using Microsoft.AspNetCore.DataProtection;

namespace ShiftManager.Services;

/// <summary>
/// Service for encrypting and decrypting sensitive configuration data using ASP.NET Data Protection API
/// </summary>
public class EncryptionService : IEncryptionService
{
    private readonly IDataProtector _protector;
    private readonly ILogger<EncryptionService> _logger;

    public EncryptionService(IDataProtectionProvider dataProtectionProvider, ILogger<EncryptionService> logger)
    {
        // Create a protector with a specific purpose string for email configuration
        // This ensures that encrypted data from one purpose cannot be decrypted by another
        _protector = dataProtectionProvider.CreateProtector("ShiftManager.EmailConfig.v1");
        _logger = logger;
    }

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return null;
        }

        try
        {
            return _protector.Protect(plainText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt data");
            throw;
        }
    }

    public string? Decrypt(string? encryptedText)
    {
        if (string.IsNullOrWhiteSpace(encryptedText))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(encryptedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt data. The data may have been encrypted with a different key or corrupted.");
            throw;
        }
    }
}
