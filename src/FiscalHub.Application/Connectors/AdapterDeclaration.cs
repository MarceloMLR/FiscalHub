namespace FiscalHub.Application.Connectors;

/// <summary>
/// A declaração de um adapter de saída (change <c>platform-account-and-connectors</c>, D3 e D17; ADR-0037: "o adapter
/// declara, o perfil habilita"): o nome dele, a plataforma a que pertence, os módulos que atende, as integrações de cada
/// módulo, a versão de base que cada integração usa e se a API dela aceita lote. É característica da plataforma, em
/// código: não é gravada no perfil — duplicar em dado o que o código já sabe criaria a pergunta "e quando divergem" —, e
/// quem configura não a edita.
/// <para>Plataforma e adapter são nomes diferentes. Hoje coincidem porque a Avalara tem um adapter só; com o AvaTax, a
/// plataforma é <c>Avalara</c> e os adapters são dois. A gravação do perfil grava os dois na forma declarada aqui, e as
/// leituras os comparam exatos (D17).</para>
/// </summary>
public interface IAdapterDeclaration
{
    /// <summary>O nome do adapter: a chave do conector.</summary>
    string Adapter { get; }

    /// <summary>A plataforma a que o adapter pertence: a da conta.</summary>
    string Platform { get; }

    /// <summary>Os módulos que o adapter atende, com as integrações de cada um.</summary>
    IReadOnlyList<DeclaredModule> Modules { get; }
}

/// <summary>Um módulo que o adapter atende (um nome de <see cref="TenantModules"/>), com as integrações dele.</summary>
public sealed record DeclaredModule(string Module, IReadOnlyList<DeclaredIntegration> Integrations);

/// <summary>
/// Uma integração declarada: a chave do vocabulário do hub (<see cref="ConnectorIntegrations"/>), a versão de base que ela
/// usa — um rótulo do mapa de URLs base do conector, que escolhe a base e nunca entra na URL (D4) — e se a API dela aceita
/// lote.
/// </summary>
public sealed record DeclaredIntegration(string Key, string Version, bool AcceptsBatch);

/// <summary>O que se pergunta a uma declaração.</summary>
public static class AdapterDeclarations
{
    /// <summary>As versões de base que o adapter declara: as que as integrações dele usam, sem repetir, na ordem.</summary>
    public static IReadOnlyList<string> VersionsOf(IAdapterDeclaration declaration)
        => [.. declaration.Modules.SelectMany(m => m.Integrations).Select(i => i.Version).Distinct(StringComparer.Ordinal)];
}
