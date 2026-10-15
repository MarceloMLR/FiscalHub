namespace FiscalHub.Application.Connectors;

/// <summary>
/// O que o <c>/info</c> responde a qualquer papel do tenant logado, para montar a barra lateral e os selos. O ambiente é o
/// da instância, e nunca o do perfil nem um padrão (change <c>platform-account-and-connectors</c>, D6). A integração
/// automática é derivada do perfil (ADR-0029), e os conectores, apresentação.
/// <para>Os <see cref="Connectors"/> são a <b>árvore que a barra lateral desenha</b> (change
/// <c>connector-navigation-and-admin-json</c>, D6): uma entrada por conector do tenant, na ordem gravada no perfil. A
/// lista plana de módulos do nível do tenant saiu junto — ela existia porque era por ela que a barra lateral era montada,
/// e não é mais; duas representações da mesma verdade na mesma resposta criariam a pergunta de qual vale. A leitura do
/// perfil (<c>GET /connector</c>) é outra resposta, e não muda.</para>
/// <para>O <see cref="CanViewRawJson"/> é a exceção: ele é <b>permissão</b> (D7). Diz, para o usuário logado, se ele pode
/// ver as fotos cruas, derivado da mesma lista de papéis que o <c>RequireRole</c> do <c>/trace</c> e do zip usa — quem a
/// conhece é o Host, e por isso ela chega aqui já respondida. É um booleano, e não a lista: o servidor já sabe quem está
/// logado, e responde a pergunta em vez de mandar os dados para a tela decidir de novo.</para>
/// </summary>
public sealed record InstanceInfo(
    string Environment, bool AutomaticIntegration, bool InboundScans, IReadOnlyList<ConnectorInfo> Connectors, bool CanViewRawJson)
{
    /// <param name="instance">O ambiente da instância.</param>
    /// <param name="profile">O perfil do tenant logado; sem perfil, valem os padrões dos selos e a árvore vazia.</param>
    /// <param name="declarations">As declarações registradas, de onde saem o rótulo e o agendamento de cada conector.</param>
    /// <param name="scanningOrigins">As origens dos feeds de mudanças registrados no host.</param>
    /// <param name="canViewRawJson">Se o usuário logado está num dos papéis que veem as fotos cruas.</param>
    public static InstanceInfo For(
        IInstanceEnvironment instance, TenantConnectorProfile? profile, IEnumerable<IAdapterDeclaration> declarations,
        IEnumerable<string> scanningOrigins, bool canViewRawJson)
    {
        string[] origins = [.. scanningOrigins];
        IReadOnlyList<IAdapterDeclaration> declared = [.. declarations];
        return new InstanceInfo(
            instance.Name,
            Inbound.AutomaticIntegration.IsOn(profile, origins),
            Inbound.AutomaticIntegration.Scans(profile, origins),
            [.. (profile?.Accounts ?? []).SelectMany(a => a.Connectors.Select(c => Of(a, c, declared)))],
            canViewRawJson);
    }

    // O conector como a barra lateral o desenha. O rótulo e o agendamento são da declaração, achada pelo par
    // (plataforma, adapter), comparado exato — é a identidade do conector (change platform-account-and-connectors, D17).
    // Sem declaração — um adapter que saiu do deploy, ou uma linha semeada por SQL — o conector continua na resposta, com
    // o adapter no lugar do rótulo e sem agendamento: sumir esconderia configuração que existe. É a mesma tolerância da
    // leitura do perfil, que já segue sem a declaração.
    private static ConnectorInfo Of(PlatformAccount account, PlatformConnector connector, IReadOnlyList<IAdapterDeclaration> declarations)
    {
        IAdapterDeclaration? declaration = declarations.FirstOrDefault(d =>
            string.Equals(d.Platform, account.Platform, StringComparison.Ordinal)
            && string.Equals(d.Adapter, connector.Adapter, StringComparison.Ordinal));

        return new ConnectorInfo(
            connector.Adapter,
            declaration?.Label ?? connector.Adapter,
            declaration?.Scheduling ?? false,
            TenantModules.Ordered(connector.Modules.Select(m => m.Module)));
    }
}

/// <summary>
/// Um conector do tenant na árvore do <c>/info</c>: o <paramref name="Adapter"/>, que é a identidade dele; o
/// <paramref name="Label"/> e o <paramref name="Scheduling"/>, que o adapter <b>declara</b> e o perfil não guarda; e os
/// <paramref name="Modules"/> ligados naquele conector, na ordem da barra lateral.
/// <para>Um conector sem módulo ligado vem com a lista vazia, e continua sendo uma entrada: quem decide que um conector
/// sem item nenhum não vira grupo é a tela (<c>module-navigation</c>).</para>
/// </summary>
public sealed record ConnectorInfo(string Adapter, string Label, bool Scheduling, IReadOnlyList<string> Modules);
