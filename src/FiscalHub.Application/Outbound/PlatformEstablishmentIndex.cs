using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Outbound;

/// <summary>
/// O resultado do casamento de um CNPJ com a listagem da plataforma (change <c>platform-establishment-resolution</c>, D2).
/// Não há forma de "um escolhido entre vários": com mais de um candidato, o resultado é <see cref="Ambiguous"/>, e a única
/// saída que ele oferece é a recusa nomeando os candidatos.
/// </summary>
public abstract record EstablishmentMatch
{
    private EstablishmentMatch()
    {
    }

    /// <summary>Nenhum estabelecimento da plataforma tem o CNPJ.</summary>
    public sealed record None : EstablishmentMatch;

    /// <summary>Exatamente um estabelecimento da plataforma tem o CNPJ.</summary>
    public sealed record Unique(PlatformEstablishment Establishment) : EstablishmentMatch;

    /// <summary>Mais de um estabelecimento da plataforma tem o CNPJ, na ordem em que a listagem os trouxe.</summary>
    public sealed record Ambiguous(IReadOnlyList<PlatformEstablishment> Candidates) : EstablishmentMatch;
}

/// <summary>
/// Os estabelecimentos da plataforma de um tenant e ambiente, indexados pelo CNPJ normalizado (D4): sem pontuação, com as
/// letras e a caixa como vieram, nos dois lados. Só o CNPJ participa do casamento — nem os códigos, nem os nomes, nem a
/// ordem do retorno. Os candidatos de um CNPJ são distintos pelo identificador da plataforma; uma linha sem identificador
/// conta à parte, porque o erro, se houver, vai para o lado de recusar mais.
/// </summary>
public sealed class PlatformEstablishmentIndex
{
    private readonly Dictionary<string, List<PlatformEstablishment>> _byTaxId;

    private PlatformEstablishmentIndex(bool canList, Dictionary<string, List<PlatformEstablishment>> byTaxId)
    {
        CanList = canList;
        _byTaxId = byTaxId;
    }

    /// <summary>O índice de quem não lista: o adapter de saída não declara a listagem, e nenhuma chamada saiu.</summary>
    public static PlatformEstablishmentIndex Unsupported { get; } = new(canList: false, new Dictionary<string, List<PlatformEstablishment>>());

    /// <summary>Falso quando o adapter de saída não sabe listar: a tabela <c>establishments</c> é a única fonte.</summary>
    public bool CanList { get; }

    /// <summary>Ao menos um estabelecimento da plataforma tem o CNPJ (a parte nossa da nota que não diz, D5).</summary>
    public bool Knows(string taxId) => _byTaxId.ContainsKey(TaxIdentifiers.Normalize(taxId));

    public EstablishmentMatch Match(string taxId)
        => _byTaxId.TryGetValue(TaxIdentifiers.Normalize(taxId), out List<PlatformEstablishment>? candidates)
            ? candidates.Count == 1 ? new EstablishmentMatch.Unique(candidates[0]) : new EstablishmentMatch.Ambiguous([.. candidates])
            : new EstablishmentMatch.None();

    internal static PlatformEstablishmentIndex From(IEnumerable<PlatformEstablishment> establishments)
    {
        var byTaxId = new Dictionary<string, List<PlatformEstablishment>>(StringComparer.Ordinal);
        foreach (PlatformEstablishment establishment in establishments)
        {
            // Sem CNPJ, nunca casaria: fica fora do índice.
            string taxId = TaxIdentifiers.Normalize(establishment.TaxId);
            if (taxId.Length == 0)
            {
                continue;
            }

            if (!byTaxId.TryGetValue(taxId, out List<PlatformEstablishment>? candidates))
            {
                byTaxId[taxId] = candidates = [];
            }

            if (establishment.PlatformId is { } id && candidates.Exists(c => c.PlatformId == id))
            {
                continue;   // o mesmo estabelecimento listado de novo: um candidato só
            }

            candidates.Add(establishment with { TaxId = taxId });
        }

        return new PlatformEstablishmentIndex(canList: true, byTaxId);
    }
}
