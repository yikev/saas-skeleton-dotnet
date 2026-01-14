using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace SaaSSkeleton.Domain.Auth;
public static class RefreshTokenGenerator
{
    public static string GenerateRefreshToken()
    {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);

        
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public static string HashToken(string token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(token);
        using var sha256 = SHA256.Create();

        byte[] hashBytes = sha256.ComputeHash(bytes);

        return Convert.ToHexString(hashBytes);
    }
}