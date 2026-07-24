using System.Security.Cryptography;
using System.Text;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Impressão do conteúdo cru de um documento (SHA-256 em hex). Usada pela idempotência por conteúdo:
/// mesmo cru → mesmo hash (duplicata de entrega, ignora); cru diferente → hash diferente (correção,
/// reintegra). Calculada sobre o texto exato da origem, sem normalizar.
/// </summary>
public static class ContentFingerprint
{
    public static string Of(string rawContent)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawContent)));
}
