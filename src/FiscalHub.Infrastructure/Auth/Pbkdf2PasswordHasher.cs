using System.Security.Cryptography;

namespace FiscalHub.Infrastructure.Auth;

/// <summary>
/// Hash de senha com PBKDF2 (SHA-256, salt aleatório por senha, muitas iterações) — sem dependência
/// externa, usando só a BCL. Formato guardado: <c>iterações.saltBase64.hashBase64</c>. A verificação
/// usa comparação de tempo constante pra não vazar por timing.
/// </summary>
internal static class Pbkdf2PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string stored, string password)
    {
        string[] parts = stored.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out int iterations))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] key = Convert.FromBase64String(parts[2]);
        byte[] attempt = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, key.Length);
        return CryptographicOperations.FixedTimeEquals(attempt, key);
    }
}
