using FiscalHub.Application.Auth;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Queries;
using FiscalHub.Domain.Envelope;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Lado de leitura em EF Core: projeta o rastreio para a visão do dashboard, escopado por tenant.</summary>
internal sealed class SqlDocumentQueries : IDocumentQueries
{
    // As faixas de status dos cards e da tabela, num lugar só (change erp-company-directory-and-card-filters, D9). A ignorada
    // conta no total e em nenhuma faixa: não é erro (spec document-grouping).
    private static readonly IntegrationStatus[] Finished = [IntegrationStatus.Confirmed];
    private static readonly IntegrationStatus[] Processing = [IntegrationStatus.Submitted, IntegrationStatus.Pending];
    private static readonly IntegrationStatus[] Failed = [IntegrationStatus.IntegrationError, IntegrationStatus.DeadLettered, IntegrationStatus.Unconfirmed];

    private readonly ProcessingDbContext _db;
    private readonly ITenantContext _tenant;

    public SqlDocumentQueries(ProcessingDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListRecentAsync(int limit, CancellationToken ct = default)
        => await _db.ProcessedDocuments
            .Where(d => d.TenantId == _tenant.TenantId)
            .OrderByDescending(d => d.Id)
            .Take(limit)
            .Select(d => new DocumentSummary
            {
                TenantId = d.TenantId,
                NaturalKey = d.NaturalKey,
                Type = d.Type,
                Status = d.Status,
                Attempts = d.Attempts,
                ExternalId = d.ExternalId,
                Reason = d.Reason,
                Number = d.DocumentNumber,
                Model = d.DocumentModel,
                UpdatedAt = d.UpdatedAt,
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DocumentGroup>> ListGroupsAsync(int limit, CancellationToken ct = default)
        => await GroupsQuery(limit).ToListAsync(ct);

    public async Task<IReadOnlyList<ModelTotals>> CountByModelAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => await TotalsQuery(from, to).ToListAsync(ct);

    /// <summary>A consulta da tabela, exposta ao teste que confere a tradução para o SQL Server.</summary>
    internal IQueryable<DocumentGroup> GroupsQuery(int limit)
        => Grouped()
            .GroupBy(d => new { d.CompanyCode, d.BranchCode, d.ReferenceDate, d.Type, d.DocumentModel, d.Trigger })
            .OrderByDescending(g => g.Key.ReferenceDate)
            .ThenBy(g => g.Key.CompanyCode)
            .ThenBy(g => g.Key.BranchCode)
            .Select(g => new DocumentGroup
            {
                CompanyCode = g.Key.CompanyCode!,
                BranchCode = g.Key.BranchCode ?? string.Empty,
                ReferenceDate = g.Key.ReferenceDate!,
                Type = g.Key.Type,
                Model = g.Key.DocumentModel,
                Trigger = g.Key.Trigger ?? SqlProcessingStore.AutomaticMode,
                Total = g.Count(),
                Finalizadas = g.Count(x => Finished.Contains(x.Status)),
                EmProcessamento = g.Count(x => Processing.Contains(x.Status)),
                ComErro = g.Count(x => Failed.Contains(x.Status)),
            })
            .Take(limit);

    /// <summary>A consulta dos cards, exposta ao teste que confere a tradução para o SQL Server.</summary>
    internal IQueryable<ModelTotals> TotalsQuery(DateOnly from, DateOnly to)
    {
        // A data de referência é texto yyyy-MM-dd, cuja ordem é a da data.
        string first = from.ToString("yyyy-MM-dd"), last = to.ToString("yyyy-MM-dd");
        return Grouped()
            .Where(d => string.Compare(d.ReferenceDate, first) >= 0 && string.Compare(d.ReferenceDate, last) <= 0)
            .GroupBy(d => d.DocumentModel)
            .OrderBy(g => g.Key)
            .Select(g => new ModelTotals
            {
                Model = g.Key,
                Total = g.Count(),
                Finalizadas = g.Count(x => Finished.Contains(x.Status)),
                EmProcessamento = g.Count(x => Processing.Contains(x.Status)),
                ComErro = g.Count(x => Failed.Contains(x.Status)),
            });
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListByGroupAsync(
        string companyCode, string branchCode, string referenceDate, DocumentType? type, string? model, string? trigger,
        CancellationToken ct = default)
    {
        IQueryable<ProcessedDocument> query = _db.ProcessedDocuments
            .Where(d => d.TenantId == _tenant.TenantId
                && d.CompanyCode == companyCode && d.BranchCode == branchCode && d.ReferenceDate == referenceDate);

        if (type is { } t)
        {
            query = query.Where(d => d.Type == t);
        }

        if (model is not null)
        {
            query = query.Where(d => d.DocumentModel == model);
        }

        if (trigger is not null)
        {
            // O modo Automatic casa também o nulo do registro antigo, como os grupos o servem.
            query = trigger == SqlProcessingStore.AutomaticMode
                ? query.Where(d => d.Trigger == null || d.Trigger == trigger)
                : query.Where(d => d.Trigger == trigger);
        }

        return await query
            .OrderByDescending(d => d.Id)
            .Select(d => new DocumentSummary
            {
                TenantId = d.TenantId,
                NaturalKey = d.NaturalKey,
                Type = d.Type,
                Status = d.Status,
                Attempts = d.Attempts,
                ExternalId = d.ExternalId,
                Reason = d.Reason,
                Number = d.DocumentNumber,
                Model = d.DocumentModel,
                UpdatedAt = d.UpdatedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListByKeysAsync(
        string tenantId, IReadOnlyList<string> naturalKeys, CancellationToken ct = default)
        => await _db.ProcessedDocuments
            .Where(d => d.TenantId == tenantId && naturalKeys.Contains(d.NaturalKey))
            .Select(d => new DocumentSummary
            {
                TenantId = d.TenantId,
                NaturalKey = d.NaturalKey,
                Type = d.Type,
                Status = d.Status,
                Attempts = d.Attempts,
                ExternalId = d.ExternalId,
                Reason = d.Reason,
                Number = d.DocumentNumber,
                Model = d.DocumentModel,
                UpdatedAt = d.UpdatedAt,
            })
            .ToListAsync(ct);

    // As notas do tenant logado que têm grupo: a referência antiga, sem grupo, fica fora da tabela e dos cards.
    private IQueryable<ProcessedDocument> Grouped()
        => _db.ProcessedDocuments.Where(d => d.TenantId == _tenant.TenantId && d.CompanyCode != null && d.ReferenceDate != null);
}
