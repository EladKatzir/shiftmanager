# FINDING-011: API Password Hashing Uses HMACSHA512 Instead of PBKDF2

| Field | Value |
|-------|-------|
| **ID** | FINDING-011 |
| **Date** | 2026-02-18 |
| **Category** | Security / Authentication |
| **Severity** | MEDIUM |

## Expected Behavior

All password hashing in the application should use the same `PasswordHasher` class which implements PBKDF2-SHA256 with 100,000 iterations, 16-byte salt, and 32-byte hash.

## Actual Behavior

`UserApiService.cs` (lines 156-159) uses `HMACSHA512` for password hashing when creating users via the API, instead of the standard `PasswordHasher.CreateHash()` method.

## Evidence

**UserApiService.cs** (lines 156-159):
```csharp
using var hmac = new HMACSHA512();
user.PasswordHash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(password)));
user.PasswordSalt = Convert.ToBase64String(hmac.Key);
```

**PasswordHasher.cs** (standard implementation):
```csharp
public static string CreateHash(string password)
{
    using var rng = RandomNumberGenerator.Create();
    var salt = new byte[16];
    rng.GetBytes(salt);
    var hash = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256)
        .GetBytes(32);
    // Returns formatted string with salt and hash
}
```

**Impact:**
- Users created via API have HMACSHA512 hashes
- Users created via admin page or signup have PBKDF2 hashes
- Login verification in `PasswordHasher.VerifyHash()` expects PBKDF2 format
- **Users created via API cannot log in via the web UI** (hash format mismatch)

## Root Cause

The API service was implemented independently of the existing password infrastructure. The developer used raw HMACSHA512 instead of calling the established `PasswordHasher` utility.

## Fix Recommendation

Replace the HMACSHA512 code in `UserApiService.cs` with:

```csharp
user.PasswordHash = PasswordHasher.CreateHash(password);
// Remove PasswordSalt usage — PasswordHasher embeds salt in the hash string
```

Also verify and migrate any existing API-created users' password hashes.

## Verification Plan

1. Create a user via the API
2. Attempt to log in via the web UI
3. Before fix: Login fails (hash format mismatch)
4. After fix: Login succeeds

## Confidence

**95%** — Code review clearly shows different hashing algorithms. The hash format mismatch means API-created users cannot authenticate via the standard login flow.
