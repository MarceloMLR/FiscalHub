using FiscalHub.Application.Connectors;

namespace FiscalHub.Host;

/// <summary>
/// A guarda da <b>chave do conector</b> na subida do host (change <c>connector-navigation-and-admin-json</c>, tarefa
/// 4.0). Não é regra nova: o <see cref="IAdapterDeclaration.Adapter"/> já se declara "o nome do adapter: a chave do
/// conector", e o <see cref="IAdapterDeclaration.Label"/>, "a identidade do conector continua sendo o
/// <c>Adapter</c>, comparado exato". O que faltava era quem garantisse.
/// <para>A unicidade só era conferida <b>dentro de uma conta</b>: o <c>ConnectorProfileService</c> recusa "o adapter
/// '…' aparece N vezes, e a conta tem um conector por adapter" — por conta, e não entre as declarações do deploy. Duas
/// declarações de plataformas diferentes com o mesmo nome de adapter passavam. Acontecendo, o <c>/info</c> devolveria
/// duas entradas com o mesmo <c>adapter</c> e a barra lateral ficaria com duas chaves iguais: dois grupos
/// indistinguíveis, e o item (conector, módulo) de um abrindo a tela do outro — que é o descuido que o ADR-0039 avisa,
/// e em silêncio.</para>
/// <para>Hoje não é alcançável (uma plataforma, um adapter), e o caminho de crescimento escrito na própria interface
/// mantém os nomes distintos: "com o AvaTax, a plataforma é Avalara e os adapters são dois". É por isso que trava
/// agora: enquanto é verdade, é barato. <b>Dois adapters de uma mesma plataforma continuam valendo</b> — o que se
/// recusa é o nome repetido.</para>
/// </summary>
public static class AdapterDeclarationGuard
{
    /// <summary>
    /// Confere as declarações reunidas no contêiner do host. Repetido, o host <b>não sobe</b>: uma chave ambígua
    /// escolhida em silêncio na primeira leitura é pior do que uma subida que falha dizendo qual pacote tirar.
    /// </summary>
    /// <exception cref="InvalidOperationException">Um nome de adapter declarado mais de uma vez.</exception>
    public static IServiceProvider EnsureDistinctAdapterDeclarations(this IServiceProvider services)
    {
        EnsureDistinct(services.GetServices<IAdapterDeclaration>());
        return services;
    }

    /// <summary>
    /// A regra, sobre as declarações. Comparação <b>exata</b> (<see cref="StringComparer.Ordinal"/>), como todas as
    /// outras do par (plataforma, adapter): a chave é o nome como o adapter o escreve.
    /// </summary>
    public static void EnsureDistinct(IEnumerable<IAdapterDeclaration> declarations)
    {
        IReadOnlyList<IAdapterDeclaration> declared = [.. declarations];

        string[] repeated =
        [
            .. declared
                .GroupBy(d => d.Adapter, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                // A mensagem nomeia o adapter e as plataformas que o declaram, na ordem em que foram registradas: é o
                // que quem sobe o host precisa para saber qual dos dois pacotes sai.
                .Select(g => $"o adapter '{g.Key}' é declarado por {string.Join(", ", g.Select(d => $"'{d.Platform}'"))}"),
        ];

        if (repeated.Length > 0)
        {
            throw new InvalidOperationException(
                $"Declarações de adapter: {string.Join("; ", repeated)}. O nome do adapter é a chave do conector, e dois "
                + "conectores com a mesma chave ficariam indistinguíveis na barra lateral. Plataformas diferentes "
                + "declaram adapters de nomes diferentes.");
        }
    }
}
