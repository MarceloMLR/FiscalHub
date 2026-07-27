namespace FiscalHub.Host;

/// <summary>Configuração do JWT (seção "Jwt"). Em produção, a chave é um segredo, não fica no appsettings.</summary>
public sealed class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "FiscalHub";
    public string Audience { get; set; } = "FiscalHub.Dashboard";
    public int ExpiryMinutes { get; set; } = 480;
}
