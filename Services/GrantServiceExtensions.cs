using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Convenience helpers built on top of <see cref="IGrantService"/>.
/// </summary>
public static class GrantServiceExtensions
{
    /// <summary>
    /// Resolves the localization NameKeys for the supplied grant keys, preserving the order of
    /// <paramref name="grantKeys"/> and skipping any key that does not resolve. Used to tell a user
    /// which grant(s) would unlock a read-only calendar. Reads the authoritative
    /// <see cref="GrantType.NameKey"/> stored on each grant type rather than assuming the
    /// "Grant_{Key}" naming convention.
    /// </summary>
    public static async Task<List<string>> GetGrantNameKeysAsync(this IGrantService grantService, params string[] grantKeys)
    {
        if (grantKeys is null || grantKeys.Length == 0)
            return new List<string>();

        var allGrantTypes = await grantService.GetAllGrantTypesAsync();

        var nameKeyByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var gt in allGrantTypes)
            nameKeyByKey[gt.Key] = gt.NameKey;

        var result = new List<string>();
        foreach (var key in grantKeys)
        {
            if (nameKeyByKey.TryGetValue(key, out var nameKey) && !string.IsNullOrEmpty(nameKey))
                result.Add(nameKey);
        }
        return result;
    }
}
