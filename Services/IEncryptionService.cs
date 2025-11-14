namespace ShiftManager.Services;

/// <summary>
/// Service for encrypting and decrypting sensitive configuration data
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts a plain text string
    /// </summary>
    /// <param name="plainText">The text to encrypt</param>
    /// <returns>The encrypted text, or null if input is null/empty</returns>
    string? Encrypt(string? plainText);

    /// <summary>
    /// Decrypts an encrypted string
    /// </summary>
    /// <param name="encryptedText">The encrypted text to decrypt</param>
    /// <returns>The decrypted plain text, or null if input is null/empty</returns>
    string? Decrypt(string? encryptedText);
}
