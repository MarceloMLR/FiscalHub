namespace FiscalHub.Application.Connectors;

/// <summary>
/// O cofre dos segredos de conector (ADR-0027). A tela é a porta de entrada, e o cofre, o destino: o perfil guarda só
/// a referência. Em produção é o Key Vault; em dev, um emulador da mesma API, em memória — o adapter é o mesmo.
/// Só aceita nomes de segredo de conector (<see cref="SecretNames.Prefix"/>).
/// </summary>
public interface ISecretStore
{
    /// <summary>O valor do segredo, ou <c>null</c> se ele não existe (vazio conta como ausente).</summary>
    Task<string?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Grava uma versão nova do segredo.</summary>
    Task SetAsync(string name, string value, CancellationToken ct = default);

    /// <summary>Se o segredo existe e quando foi gravado, sem ler o valor. <c>null</c> se ele não existe.</summary>
    Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default);
}

/// <summary>O que se diz de um segredo sem ler o valor: a data da versão atual.</summary>
public sealed record SecretDescription(DateTimeOffset? UpdatedOn);
