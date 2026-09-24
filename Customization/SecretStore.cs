using System;
using System.Security.Cryptography;
using System.Text;

namespace EndfieldChargePlus.Customization;

public static class SecretStore
{
    public static string Protect(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return "";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch { return ""; }
    }

    public static string Unprotect(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return "";
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return ""; }
    }
}
