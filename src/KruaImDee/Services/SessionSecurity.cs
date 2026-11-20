using System.Security.Cryptography;
using System.Text;

namespace KruaImDee.Services;

// Reject old login cookies after account changes.
public static class SessionSecurity
{
    public const string Claim = "password-version";

    public static string Fingerprint(string passwordHash)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));
    }
}
