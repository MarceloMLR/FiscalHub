namespace FiscalHub.Application.Connectors;

/// <summary>
/// A declaração de um adapter de saída (change <c>platform-account-and-connectors</c>, D3 e D17; ADR-0037: "o adapter
/// declara, o perfil habilita"): o nome dele, a plataforma a que pertence, o rótulo do conector na tela, se o conector
/// tem agendamento, os módulos que atende, as integrações de cada módulo, a versão de base que cada integração usa e se
/// a API dela aceita lote. É característica da plataforma, em código: não é gravada no perfil — duplicar em dado o que o
/// código já sabe criaria a pergunta "e quando divergem" —, e quem configura não a edita.
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

    /// <summary>
    /// O nome do conector na barra lateral e nas telas (<c>module-navigation</c>; change
    /// <c>connector-navigation-and-admin-json</c>, D3) — na Avalara, <c>Tax Compliance</c>. A chave do conector é o
    /// <b>nome do adapter</b>, e "Avalara" é a plataforma, não o produto; com o AvaTax seriam dois grupos de menu com o
    /// mesmo começo e nenhum com o nome que o ADR-0039 usa.
    /// <para>É <b>texto de tela</b>: MUST NOT entrar em chave, nome de segredo, URL nem comparação. A identidade do
    /// conector continua sendo o <see cref="Adapter"/>, comparado exato. É declarado, e não um mapa de nomes do lado da
    /// tela, para que um adapter novo não nasça sem nome no menu.</para>
    /// </summary>
    string Label { get; }

    /// <summary>
    /// Se o conector aparece com o item de Agendamento (ADR-0039 §2; design D4). Não existe agendamento para todo
    /// conector: cálculo e emissão são disparados pelo ERP, e não por horário.
    /// <para>Não é derivado de uma integração ligada: um cliente que contratou o conector e ainda não preencheu a
    /// integração continua precisando do Agendamento — é por ele que dispara a primeira carga manual.</para>
    /// </summary>
    bool Scheduling { get; }

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
