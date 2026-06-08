using FiscalHub.Domain.Mercadoria.Reforma;

namespace FiscalHub.Domain.Mercadoria;

/// <summary>
/// Item (linha) de uma NF-e de mercadoria. Carrega os dados comerciais do produto e
/// o bloco de tributos da Reforma (Grupo UB) por item.
/// </summary>
public sealed record ItemMercadoria
{
    /// <summary>Número sequencial do item na nota (nItem).</summary>
    public required int Numero { get; init; }

    /// <summary>Código do produto na origem (cProd).</summary>
    public required string CodigoProduto { get; init; }

    /// <summary>Descrição do produto (xProd).</summary>
    public required string Descricao { get; init; }

    /// <summary>NCM do produto (8 dígitos).</summary>
    public required string Ncm { get; init; }

    /// <summary>
    /// CFOP da operação (4 dígitos). É uma chave de MAPEABILIDADE: a validação de
    /// integração checa se o conector sabe traduzir este CFOP para o destino.
    /// </summary>
    public required string Cfop { get; init; }

    /// <summary>Quantidade comercializada.</summary>
    public required decimal Quantidade { get; init; }

    /// <summary>Valor unitário.</summary>
    public required decimal ValorUnitario { get; init; }

    /// <summary>Valor total do item, como consta na nota.</summary>
    public required decimal ValorTotal { get; init; }

    /// <summary>Tributos da Reforma (IBS/CBS/IS) do item — Grupo UB da NT 2025.002.</summary>
    public required TributosReforma TributosReforma { get; init; }
}
