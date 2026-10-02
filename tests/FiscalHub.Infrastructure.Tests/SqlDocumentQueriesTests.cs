using FiscalHub.Application.Outbound;
using FiscalHub.Application.Queries;
using FiscalHub.Domain.Envelope;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica as leituras dos cards e do modal (spec document-grouping, design D9 e D10), no SQLite em memória: a contagem
/// por modelo numa janela de dias fiscais, sobre todas as notas do período; o grupo com o modelo; e o modal que lista
/// exatamente a linha (empresa, filial, dia, tipo, modelo e modo).
/// </summary>
public class SqlDocumentQueriesTests
{
    private const string Sp01 = "44278225000260";

    // ---------- a contagem por modelo (os cards) ----------

    [Fact]
    public async Task Counts_by_model_within_the_inclusive_window_of_fiscal_days()
    {
        using var h = await Harness.WithAsync(
            Row("n1", "2026-08-07", "55", IntegrationStatus.IntegrationError),
            Row("n2", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse),
            Row("n3", "2026-09-05", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse),
            Row("n4", "2026-08-06", "55", IntegrationStatus.Confirmed),     // um dia antes da janela
            Row("n5", "2026-09-06", "55", IntegrationStatus.Confirmed),     // um dia depois
            Row("n6", "2016-03-05", "55", IntegrationStatus.IntegrationError));

        IReadOnlyList<ModelTotals> totals = await h.Queries.CountByModelAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 5));

        Assert.Equal(
            [
                new ModelTotals { Model = "55", Total = 1, Finalizadas = 0, EmProcessamento = 0, ComErro = 1 },
                new ModelTotals { Model = "SE", Total = 2, Finalizadas = 0, EmProcessamento = 0, ComErro = 0 },   // a ignorada não é erro
            ],
            totals);
    }

    [Fact]
    public async Task Counts_every_status_in_its_card()
    {
        using var h = await Harness.WithAsync(
            Row("n1", "2026-08-07", "55", IntegrationStatus.Confirmed),
            Row("n2", "2026-08-07", "55", IntegrationStatus.Submitted),
            Row("n3", "2026-08-07", "55", IntegrationStatus.Pending),
            Row("n4", "2026-08-07", "55", IntegrationStatus.IntegrationError),
            Row("n5", "2026-08-07", "55", IntegrationStatus.DeadLettered),
            Row("n6", "2026-08-07", "55", IntegrationStatus.Unconfirmed),
            Row("n7", "2026-08-07", "55", IntegrationStatus.Ignored));

        ModelTotals totals = Assert.Single(await h.Queries.CountByModelAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7)));

        Assert.Equal(new ModelTotals { Model = "55", Total = 7, Finalizadas = 1, EmProcessamento = 2, ComErro = 3 }, totals);
    }

    [Fact]
    public async Task Counts_only_the_tenant_of_the_login_and_only_notes_with_a_group()
    {
        ProcessedDocument withoutGroup = Row("n3", "2026-08-07", "55", IntegrationStatus.Confirmed);
        withoutGroup.CompanyCode = null;   // referência antiga, sem o grupo
        using var h = await Harness.WithAsync(
            Row("n1", "2026-08-07", "55", IntegrationStatus.Confirmed),
            Row("n2", "2026-08-07", "55", IntegrationStatus.Confirmed, tenant: "tenant-b"),
            withoutGroup);

        ModelTotals totals = Assert.Single(await h.Queries.CountByModelAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7)));

        Assert.Equal(1, totals.Total);
    }

    [Fact]
    public async Task Counts_more_groups_than_the_table_shows()
    {
        // 250 grupos no mesmo dia (um por filial): a tabela traz 200, e a contagem dos cards conta todos.
        using var h = await Harness.WithAsync([.. Enumerable.Range(1, 250).Select(i => Row($"n{i}", "2026-09-05", "55", IntegrationStatus.Confirmed, branch: $"F{i:000}"))]);

        Assert.Equal(200, (await h.Queries.ListGroupsAsync(200)).Count);
        Assert.Equal(250, Assert.Single(await h.Queries.CountByModelAsync(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 5))).Total);
    }

    // ---------- o grupo com o modelo (a tabela) ----------

    [Fact]
    public async Task Group_carries_the_model_and_two_models_on_the_same_day_are_two_rows()
    {
        using var h = await Harness.WithAsync(
            Row("n1", "2026-08-07", "55", IntegrationStatus.IntegrationError),
            Row("n2", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse));

        IReadOnlyList<DocumentGroup> groups = await h.Queries.ListGroupsAsync(50);

        Assert.Equal(["55", "SE"], groups.Select(g => g.Model).Order());
        Assert.All(groups, g => Assert.Equal(1, g.Total));
    }

    [Fact]
    public async Task Same_type_with_two_models_is_two_rows()
    {
        // Um modelTypes do tenant que leva dois modelos ao mesmo tipo: o modelo separa as linhas.
        using var h = await Harness.WithAsync(
            Row("n1", "2026-08-07", "55", IntegrationStatus.Confirmed),
            Row("n2", "2026-08-07", "65", IntegrationStatus.Confirmed));

        Assert.Equal(2, (await h.Queries.ListGroupsAsync(50)).Count);
    }

    [Fact]
    public async Task Alphanumeric_company_is_grouped_and_counted_as_it_came()
    {
        using var h = await Harness.WithAsync(Row("n1", "2026-08-07", "55", IntegrationStatus.Confirmed, company: "12ABC34501DE35"));

        Assert.Equal("12ABC34501DE35", Assert.Single(await h.Queries.ListGroupsAsync(50)).CompanyCode);
        Assert.Single(await h.Queries.ListByGroupAsync("12ABC34501DE35", "SP-01", "2026-08-07", null, null, null));
    }

    // ---------- o modal (a linha inteira) ----------

    [Fact]
    public async Task Modal_lists_only_the_notes_of_the_row_type_and_model()
    {
        using var h = await Harness.WithAsync(
            Row("nfe", "2026-08-07", "55", IntegrationStatus.IntegrationError),
            Row("nfse", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse));

        IReadOnlyList<DocumentSummary> nfe = await h.Queries.ListByGroupAsync(Sp01, "SP-01", "2026-08-07", DocumentType.GoodsInvoice55, "55", "Automatic");
        IReadOnlyList<DocumentSummary> nfse = await h.Queries.ListByGroupAsync(Sp01, "SP-01", "2026-08-07", DocumentType.ServiceNfse, "SE", "Automatic");

        Assert.Equal(["nfe"], nfe.Select(d => d.NaturalKey));
        Assert.Equal(["nfse"], nfse.Select(d => d.NaturalKey));
    }

    [Fact]
    public async Task Modal_lists_only_the_notes_of_the_row_mode_and_automatic_matches_the_null_mode()
    {
        ProcessedDocument old = Row("old", "2026-08-07", "55", IntegrationStatus.Confirmed);
        old.Trigger = null;   // gravada antes de o upsert gravar o modo: servida como Automatic
        using var h = await Harness.WithAsync(
            Row("auto", "2026-08-07", "55", IntegrationStatus.Confirmed),
            Row("manual", "2026-08-07", "55", IntegrationStatus.Confirmed, trigger: "Manual"),
            old);

        IReadOnlyList<DocumentSummary> automatic = await h.Queries.ListByGroupAsync(Sp01, "SP-01", "2026-08-07", DocumentType.GoodsInvoice55, "55", "Automatic");
        IReadOnlyList<DocumentSummary> manual = await h.Queries.ListByGroupAsync(Sp01, "SP-01", "2026-08-07", DocumentType.GoodsInvoice55, "55", "Manual");

        Assert.Equal(["auto", "old"], automatic.Select(d => d.NaturalKey).Order());
        Assert.Equal(["manual"], manual.Select(d => d.NaturalKey));
    }

    [Fact]
    public async Task Modal_without_filters_lists_the_whole_day_of_the_establishment_as_before()
    {
        using var h = await Harness.WithAsync(
            Row("nfe", "2026-08-07", "55", IntegrationStatus.IntegrationError),
            Row("nfse", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse, trigger: "Manual"));

        Assert.Equal(2, (await h.Queries.ListByGroupAsync(Sp01, "SP-01", "2026-08-07", null, null, null)).Count);
    }

    [Fact]
    public async Task Modal_count_matches_the_row_total()
    {
        using var h = await Harness.WithAsync(
            Row("se-1", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse),
            Row("se-2", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse),
            Row("se-3", "2026-08-07", "SE", IntegrationStatus.IntegrationError, DocumentType.ServiceNfse),
            Row("nfe", "2026-08-07", "55", IntegrationStatus.Confirmed));

        DocumentGroup row = Assert.Single(await h.Queries.ListGroupsAsync(50), g => g.Model == "SE");
        IReadOnlyList<DocumentSummary> listed = await h.Queries.ListByGroupAsync(row.CompanyCode, row.BranchCode, row.ReferenceDate, row.Type, row.Model, row.Trigger);
        ModelTotals card = Assert.Single(await h.Queries.CountByModelAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7)), t => t.Model == "SE");

        Assert.Equal(3, row.Total);
        Assert.Equal(row.Total, listed.Count);   // as ignoradas incluídas
        Assert.Equal(row.Total, card.Total);
    }

    // ---------- o banco de produção ----------

    [Fact]
    public void Sql_server_translates_the_card_and_table_queries()
    {
        // Os testes rodam no SQLite, e o host no SQL Server. As faixas de status são coleções dentro do agregado do
        // agrupamento: este teste gera o SQL do SQL Server, sem conexão, para uma tradução que falhe aparecer aqui.
        using var db = new ProcessingDbContext(new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseSqlServer("Server=localhost;Database=traducao;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var queries = new SqlDocumentQueries(db, new StubTenantContext("tenant-a"));

        string totals = queries.TotalsQuery(new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 5)).ToQueryString();
        string groups = queries.GroupsQuery(200).ToQueryString();

        Assert.Contains("GROUP BY", totals);
        Assert.Contains("[ReferenceDate] >= ", totals);
        Assert.Contains("GROUP BY", groups);
        Assert.Contains("[DocumentModel]", groups);
    }

    // ---------- apoio ----------

    private static ProcessedDocument Row(
        string key, string date, string model, IntegrationStatus status, DocumentType type = DocumentType.GoodsInvoice55,
        string tenant = "tenant-a", string company = Sp01, string branch = "SP-01", string? trigger = "Automatic") => new()
    {
        TenantId = tenant,
        NaturalKey = key,
        Type = type,
        Status = status,
        CompanyCode = company,
        BranchCode = branch,
        ReferenceDate = date,
        DocumentModel = model,
        Trigger = trigger,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn) : IDisposable
    {
        public SqlDocumentQueries Queries { get; } = new(db, new StubTenantContext("tenant-a"));

        public static async Task<Harness> WithAsync(params ProcessedDocument[] rows)
        {
            var conn = new SqliteConnection("DataSource=:memory:");
            conn.Open();
            var db = new ProcessingDbContext(new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlite(conn).Options);
            db.Database.EnsureCreated();
            db.ProcessedDocuments.AddRange(rows);
            await db.SaveChangesAsync();
            return new Harness(db, conn);
        }

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
