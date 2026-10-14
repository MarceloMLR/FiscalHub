namespace FiscalHub.Application.Connectors;

/// <summary>
/// O vocabulário das integrações (change <c>platform-account-and-connectors</c>, D3; ADR-0037): a chave de cada integração
/// é do hub, e não do adapter, como os nomes de módulo (<see cref="TenantModules"/>). Dois adapters que atendem a mesma
/// integração a declaram com a mesma chave, e é essa chave que a regra de um conector por integração compara — se ela
/// fosse do adapter, duas integrações de adapters diferentes nunca colidiriam.
/// <para>A chave é gravada (a integração ligada vira linha), e por isso é estável: minúsculas, dígitos e hífen.</para>
/// </summary>
public static class ConnectorIntegrations
{
    /// <summary>Fiscal: a nota já emitida no ERP, de entrada ou de saída.</summary>
    public const string Invoices = "notas-fiscais";

    /// <summary>Contábil: o plano de contas.</summary>
    public const string ChartOfAccounts = "plano-de-contas";

    /// <summary>Contábil: os lançamentos.</summary>
    public const string JournalEntries = "lancamentos";

    /// <summary>Contábil: os saldos.</summary>
    public const string Balances = "saldos";

    /// <summary>Inventário: o registro K200 do Bloco K (estoque escriturado).</summary>
    public const string BlockK200 = "k200";

    /// <summary>Inventário: os registros K210 e K215 do Bloco K, que vão juntos.</summary>
    public const string BlockK210K215 = "k210-k215";

    /// <summary>Inventário: o registro K230 do Bloco K.</summary>
    public const string BlockK230 = "k230";

    /// <summary>As chaves que o hub conhece, na ordem dos módulos.</summary>
    public static IReadOnlyList<string> Known { get; } =
        [Invoices, ChartOfAccounts, JournalEntries, Balances, BlockK200, BlockK210K215, BlockK230];
}
