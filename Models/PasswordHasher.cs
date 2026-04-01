using System.Security.Cryptography;

namespace ShiftManager.Models;

public static class PasswordHasher
{
    public static (byte[] hash, byte[] salt) CreateHash(string password)
    {
        using var rng = RandomNumberGenerator.Create();
        byte[] salt = new byte[16];
        rng.GetBytes(salt);
        using var derive = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        return (derive.GetBytes(32), salt);
    }

    public static bool Verify(string password, byte[] hash, byte[] salt)
    {
        // SSO users (Griffin ADFS) have empty hash/salt — no local password was set.
        // Rfc2898DeriveBytes requires salt >= 8 bytes, so return false immediately.
        if (hash == null || salt == null || hash.Length == 0 || salt.Length < 8)
            return false;

        using var derive = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        return CryptographicOperations.FixedTimeEquals(hash, derive.GetBytes(32));
    }
}
