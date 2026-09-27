using FiscalHub.Application.Auth;
using FiscalHub.Application.Support;

namespace FiscalHub.Application.Tracing;

/// <summary>
/// As fotos de rastreabilidade de um documento para quem está logado (ADR-0028). O tenant da rota é comparado com
/// o do usuário: sendo outro, a resposta é a mesma de um documento sem fotos — não se confirma a existência de nota
/// alheia — e o armazenamento do outro tenant nem é lido. Reusa a leitura das fotos do chamado de suporte.
/// </summary>
public sealed class DocumentTraceQuery
{
    private readonly INoteTraceReader _traces;
    private readonly ITenantContext _tenant;

    public DocumentTraceQuery(INoteTraceReader traces, ITenantContext tenant)
    {
        _traces = traces;
        _tenant = tenant;
    }

    /// <summary>Os arquivos do documento, ou <c>null</c> se ele é de outro tenant ou não tem fotos.</summary>
    public async Task<IReadOnlyList<TraceFile>?> GetAsync(string tenantId, string naturalKey, CancellationToken ct = default)
    {
        if (!string.Equals(tenantId, _tenant.TenantId, StringComparison.Ordinal))
        {
            return null;
        }

        IReadOnlyList<TraceFile> files = await _traces.ReadAsync(tenantId, naturalKey, ct);
        return files.Count == 0 ? null : files;
    }
}
