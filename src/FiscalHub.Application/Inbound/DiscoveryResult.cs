namespace FiscalHub.Application.Inbound;

/// <summary>
/// O que a descoberta por período achou: as referências e, quando o escopo pedido resolveu exatamente um estabelecimento,
/// o CNPJ dele, sem a pontuação e com as letras (change explicit-credential-and-execution-cnpj, D5). O CNPJ é decidido pelo
/// escopo, antes de ler as notas: vale com zero notas, e é o do cadastro da origem, e não o que as notas trazem. A execução o
/// grava, e a tabela de execuções o mostra no lugar da empresa.
/// </summary>
/// <param name="References">As referências achadas, na ordem da origem.</param>
/// <param name="EstablishmentTaxId">
/// O CNPJ do único estabelecimento do escopo; <c>null</c> quando o escopo tem mais de um, nenhum, ou a origem não o conhece.
/// </param>
public sealed record DiscoveryResult(IReadOnlyList<DocumentReference> References, string? EstablishmentTaxId = null);
