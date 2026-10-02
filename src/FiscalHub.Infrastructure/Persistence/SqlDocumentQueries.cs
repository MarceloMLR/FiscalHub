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
                Reprocessings = d.ReprocessCount,
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DocumentGroup>> ListGroupsAsync(int limit, GroupWindow? window = null, CancellationToken ct = default)
        => await GroupsQuery(limit, window).ToListAsync(ct);

    public async Task<IReadOnlyList<ModelTotals>> CountByModelAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => await TotalsQuery(from, to).ToListAsync(ct);

    /// <summary>
    /// A consulta da tabela, exposta ao teste que confere a tradução para o SQL Server. O grupo é pela execução que trouxe as
    /// notas (o dia e o período), e a janela é a mesma dos cards (D15).
    /// </summary>
    internal IQueryable<DocumentGroup> GroupsQuery(int limit, GroupWindow? window = null)
        => InWindow(Grouped(), window)
            .GroupBy(d => new { d.CompanyCode, d.BranchCode, d.ExecutedOn, d.PeriodStart, d.PeriodEnd, d.Type, d.DocumentModel, d.Trigger })
            .OrderByDescending(g => g.Key.ExecutedOn)
            .ThenBy(g => g.Key.CompanyCode)
            .ThenBy(g => g.Key.BranchCode)
            .ThenBy(g => g.Key.PeriodStart)
            .Select(g => new DocumentGroup
            {
                CompanyCode = g.Key.CompanyCode!,
                BranchCode = g.Key.BranchCode ?? string.Empty,
                ExecutedOn = g.Key.ExecutedOn!,
                PeriodStart = g.Key.PeriodStart,
                PeriodEnd = g.Key.PeriodEnd,
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
        => InWindow(Grouped(), new GroupWindow(from, to, null))
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

    public async Task<IReadOnlyList<DocumentSummary>> ListByGroupAsync(GroupKey group, CancellationToken ct = default)
    {
        IQueryable<ProcessedDocument> query = _db.ProcessedDocuments
            .Where(d => d.TenantId == _tenant.TenantId
                && d.CompanyCode == group.CompanyCode && d.BranchCode == group.BranchCode && d.ExecutedOn == group.ExecutedOn);

        if (group.Type is { } t)
        {
            query = query.Where(d => d.Type == t);
        }

        if (group.Model is { } model)
        {
            query = query.Where(d => d.DocumentModel == model);
        }

        if (group.Trigger is { } trigger)
        {
            // O modo Automatic casa também o nulo do registro antigo, como os grupos o servem.
            query = trigger == SqlProcessingStore.AutomaticMode
                ? query.Where(d => d.Trigger == null || d.Trigger == trigger)
                : query.Where(d => d.Trigger == trigger);
        }

        if (group.MatchPeriod)
        {
            // A linha da automática não tem período; a da integração tem o dela.
            query = query.Where(d => d.PeriodStart == group.PeriodStart && d.PeriodEnd == group.PeriodEnd);
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
                Reprocessings = d.ReprocessCount,
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
                Reprocessings = d.ReprocessCount,
            })
            .ToListAsync(ct);

    // As notas do tenant logado que têm grupo e dia da execução: a referência antiga, sem grupo, fica fora da tabela e dos cards.
    private IQueryable<ProcessedDocument> Grouped()
        => _db.ProcessedDocuments.Where(d => d.TenantId == _tenant.TenantId && d.CompanyCode != null && d.ExecutedOn != null);

    // A janela de dias da execução, inclusive, e o modelo. O dia é texto aaaa-mm-dd, cuja ordem é a da data.
    private static IQueryable<ProcessedDocument> InWindow(IQueryable<ProcessedDocument> query, GroupWindow? window)
    {
        if (window is null)
        {
            return query;
        }

        string first = window.From.ToString("yyyy-MM-dd"), last = window.To.ToString("yyyy-MM-dd");
        query = query.Where(d => string.Compare(d.ExecutedOn, first) >= 0 && string.Compare(d.ExecutedOn, last) <= 0);
        return window.Model is { } model ? query.Where(d => d.DocumentModel == model) : query;
    }
}
