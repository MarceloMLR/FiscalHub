using FiscalHub.Infrastructure.Auth;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>Especifica o hasher: verifica a senha certa, rejeita a errada e usa salt aleatório.</summary>
public class Pbkdf2PasswordHasherTests
{
    [Fact]
    public void Roundtrips_and_rejects_wrong_password()
    {
        string hash = Pbkdf2PasswordHasher.Hash("Fiscal@123");

        Assert.True(Pbkdf2PasswordHasher.Verify(hash, "Fiscal@123"));
        Assert.False(Pbkdf2PasswordHasher.Verify(hash, "senha-errada"));
    }

    [Fact]
    public void Same_password_hashes_differently_due_to_random_salt()
    {
        Assert.NotEqual(Pbkdf2PasswordHasher.Hash("Fiscal@123"), Pbkdf2PasswordHasher.Hash("Fiscal@123"));
    }
}
