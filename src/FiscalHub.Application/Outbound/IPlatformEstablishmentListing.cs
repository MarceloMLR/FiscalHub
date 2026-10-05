using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Outbound;

/// <summary>
/// Um estabelecimento como a plataforma de compliance o cadastra: o CNPJ e os dois códigos que o payload leva
/// (change <c>platform-establishment-resolution</c>, D1). Os códigos são texto, como vieram, e podem faltar: um candidato
/// sem código continua sendo candidato, porque filtrá-lo esconderia uma duplicidade (D4).
/// </summary>
/// <param name="TaxId">O CNPJ do estabelecimento na plataforma. O índice o normaliza (<c>TaxIdentifiers</c>).</param>
/// <param name="CompanyCode">O código da empresa na plataforma (Avalara: <c>codigoCIA</c>).</param>
/// <param name="EstablishmentCode">O código do estabelecimento na plataforma (Avalara: <c>codigo</c> do contribuinte).</param>
/// <param name="PlatformId">
/// A identidade do estabelecimento na plataforma (Avalara: <c>contribuinteId</c>): o mesmo estabelecimento listado duas
/// vezes conta uma, e é o <c>#id</c> do motivo da duplicidade (D9).
/// </param>
/// <param name="CompanyName">A descrição da empresa (Avalara: <c>descricao</c>). Só para o motivo, nunca para o casamento.</param>
public sealed record PlatformEstablishment(
    string TaxId, string? CompanyCode, string? EstablishmentCode, string? PlatformId, string? CompanyName);

/// <summary>
/// A capacidade de um adapter de saída listar os estabelecimentos da plataforma, para o de/para do estabelecimento sem
/// cadastro manual (change <c>platform-establishment-resolution</c>, D1). É opcional, no desenho do
/// <see cref="IConnectorCredentialTest"/>: o adapter que sabe listar registra a implementação com o nome dele, e quem não
/// registra não lista — a tabela <c>establishments</c> continua sendo a única fonte. O contrato de despacho não a exige.
/// <para>A falha permanente (credencial negada, caminho que não existe, formato fora do verificado) é
/// <see cref="DispatchRejectedException"/>; qualquer outra exceção é indisponibilidade, como no envio.</para>
/// </summary>
public interface IPlatformEstablishmentListing
{
    /// <summary>O nome do adapter de saída, como o perfil o grava (ex.: "Avalara").</summary>
    string Adapter { get; }

    /// <summary>
    /// Os estabelecimentos da plataforma no ambiente ativo do perfil, todos, ou nenhum: uma listagem parcial lança, e não
    /// devolve (D7).
    /// </summary>
    Task<IReadOnlyList<PlatformEstablishment>> ListAsync(TenantConnectorProfile profile, CancellationToken ct = default);
}
