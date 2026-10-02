using FiscalHub.Application.Outbound;
using FiscalHub.Application.Queries;
using FiscalHub.Domain.Envelope;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica as leituras dos cards, da tabela e do modal (spec document-grouping, design D9, D10 e D15), no SQLite em
/// memória: a contagem por modelo numa janela de dias da execução, sobre todas as notas do período; a tabela com a mesma
/// janela e o mesmo modelo; o grupo com o período integrado; e o modal que lista exatamente a linha (empresa, filial, dia da
/// execução, período, tipo, modelo e modo).
/// </summary>
public class SqlDocumentQueriesTests
{
    private const string Sp01 = "44278225000260";

    // ---------- a contagem por modelo (os cards) ----------

    [Fact]
    public async Task Counts_by_model_within_the_inclusive_window_of_execution_days()
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
    public async Task Cards_and_table_count_by_the_execution_day_and_not_by_the_fiscal_date()
    {
        // A conferência na tela: a integração imediata de hoje, para setembro de 2016, conta hoje.
        using var h = await Harness.WithAsync(
            Row("n1", "2026-10-02", "55", IntegrationStatus.Confirmed, trigger: "Manual", periodStart: "2016-09-01", periodEnd: "2016-09-30", fiscalDate: "2016-09-02"));

        var today = new DateOnly(2026, 10, 2);
        var fiscalDay = new DateOnly(2016, 9, 2);

        Assert.Equal(1, Assert.Single(await h.Queries.CountByModelAsync(today, today)).Total);
        Assert.Empty(await h.Queries.CountByModelAsync(fiscalDay, fiscalDay));
        Assert.Equal("2026-10-02", Assert.Single(await h.Queries.ListGroupsAsync(50, new GroupWindow(today, today, null))).ExecutedOn);
        Assert.Empty(await h.Queries.ListGroupsAsync(50, new GroupWindow(fiscalDay, fiscalDay, null)));
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
    public async Task Counts_only_the_tenant_of_the_login_and_only_notes_with_a_group_and_an_execution_day()
    {
        ProcessedDocument withoutGroup = Row("n3", "2026-08-07", "55", IntegrationStatus.Confirmed);
        withoutGroup.CompanyCode = null;   // referência antiga, sem o grupo
        ProcessedDocument withoutDay = Row("n4", "2026-08-07", "55", IntegrationStatus.Confirmed);
        withoutDay.ExecutedOn = null;      // o registro que a migração não preencheu
        using var h = await Harness.WithAsync(
            Row("n1", "2026-08-07", "55", IntegrationStatus.Confirmed),
            Row("n2", "2026-08-07", "55", IntegrationStatus.Confirmed, tenant: "tenant-b"),
            withoutGroup,
            withoutDay);

        ModelTotals totals = Assert.Single(await h.Queries.CountByModelAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7)));

        Assert.Equal(1, totals.Total);
        Assert.Single(await h.Queries.ListGroupsAsync(50));
    }

    [Fact]
    public async Task Counts_more_groups_than_the_table_shows()
    {
        // 250 grupos no mesmo dia (um por filial): a tabela traz 200, e a contagem dos cards conta todos.
        using var h = await Harness.WithAsync([.. Enumerable.Range(1, 250).Select(i => Row($"n{i}", "2026-09-05", "55", IntegrationStatus.Confirmed, branch: $"F{i:000}"))]);

        var day = new DateOnly(2026, 9, 5);
        Assert.Equal(200, (await h.Queries.ListGroupsAsync(200, new GroupWindow(day, day, null))).Count);
        Assert.Equal(250, Assert.Single(await h.Queries.CountByModelAsync(day, day)).Total);
    }

    // ---------- o grupo (a tabela) ----------

    [Fact]
    public async Task Table_follows_the_window_and_the_model_of_the_cards()
    {
        using var h = await Harness.WithAsync(
            Row("n1", "2026-09-28", "55", IntegrationStatus.Confirmed),
            Row("n2", "2026-10-02", "55", IntegrationStatus.Confirmed),
            Row("n3", "2026-10-02", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse),
            Row("n4", "2026-09-27", "55", IntegrationStatus.Confirmed));   // um dia antes da janela

        var from = new DateOnly(2026, 9, 28);
        var to = new DateOnly(2026, 10, 2);

        IReadOnlyList<DocumentGroup> all = await h.Queries.ListGroupsAsync(50, new GroupWindow(from, to, null));
        IReadOnlyList<DocumentGroup> nfe = await h.Queries.ListGroupsAsync(50, new GroupWindow(from, to, "55"));

        Assert.Equal(["2026-10-02", "2026-10-02", "2026-09-28"], all.Select(g => g.ExecutedOn));   // o dia mais recente primeiro
        Assert.Equal(["2026-10-02", "2026-09-28"], nfe.Select(g => g.ExecutedOn));
        Assert.All(nfe, g => Assert.Equal("55", g.Model));
        Assert.Equal(nfe.Sum(g => g.Total), Assert.Single(await h.Queries.CountByModelAsync(from, to), t => t.Model == "55").Total);
    }

    [Fact]
    public async Task Period_is_part_of_the_row_and_the_automatic_has_none()
    {
        using var h = await Harness.WithAsync(
            Row("auto", "2026-10-02", "55", IntegrationStatus.Confirmed),
            Row("set", "2026-10-02", "55", IntegrationStatus.Confirmed, trigger: "Manual", periodStart: "2016-09-01", periodEnd: "2016-09-30"),
            Row("mar", "2026-10-02", "55", IntegrationStatus.Confirmed, trigger: "Manual", periodStart: "2016-03-01", periodEnd: "2016-03-31"));

        IReadOnlyList<DocumentGroup> groups = await h.Queries.ListGroupsAsync(50);

        Assert.Equal(3, groups.Count);
        DocumentGroup automatic = Assert.Single(groups, g => g.Trigger == "Automatic");
        Assert.Null(automatic.PeriodStart);
        Assert.Null(automatic.PeriodEnd);
        Assert.Equal(
            [("2016-03-01", "2016-03-31"), ("2016-09-01", "2016-09-30")],
            groups.Where(g => g.Trigger == "Manual").Select(g => (g.PeriodStart, g.PeriodEnd)).Order());
    }

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
        Assert.Single(await h.Queries.ListByGroupAsync(Key("2026-08-07", company: "12ABC34501DE35")));
    }

    // ---------- o modal (a linha inteira) ----------

    [Fact]
    public async Task Modal_lists_only_the_notes_of_the_row_type_and_model()
    {
        using var h = await Harness.WithAsync(
            Row("nfe", "2026-08-07", "55", IntegrationStatus.IntegrationError),
            Row("nfse", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse));

        IReadOnlyList<DocumentSummary> nfe = await h.Queries.ListByGroupAsync(Key("2026-08-07", DocumentType.GoodsInvoice55, "55", "Automatic"));
        IReadOnlyList<DocumentSummary> nfse = await h.Queries.ListByGroupAsync(Key("2026-08-07", DocumentType.ServiceNfse, "SE", "Automatic"));

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

        IReadOnlyList<DocumentSummary> automatic = await h.Queries.ListByGroupAsync(Key("2026-08-07", DocumentType.GoodsInvoice55, "55", "Automatic"));
        IReadOnlyList<DocumentSummary> manual = await h.Queries.ListByGroupAsync(Key("2026-08-07", DocumentType.GoodsInvoice55, "55", "Manual"));

        Assert.Equal(["auto", "old"], automatic.Select(d => d.NaturalKey).Order());
        Assert.Equal(["manual"], manual.Select(d => d.NaturalKey));
    }

    [Fact]
    public async Task Modal_with_the_period_lists_only_that_execution_and_none_lists_the_automatic()
    {
        using var h = await Harness.WithAsync(
            Row("auto", "2026-10-02", "55", IntegrationStatus.Confirmed),
            Row("set", "2026-10-02", "55", IntegrationStatus.Confirmed, trigger: "Manual", periodStart: "2016-09-01", periodEnd: "2016-09-30"),
            Row("mar", "2026-10-02", "55", IntegrationStatus.Confirmed, trigger: "Manual", periodStart: "2016-03-01", periodEnd: "2016-03-31"));

        GroupKey manual = Key("2026-10-02", DocumentType.GoodsInvoice55, "55", "Manual");

        Assert.Equal(["set"], (await h.Queries.ListByGroupAsync(manual with { MatchPeriod = true, PeriodStart = "2016-09-01", PeriodEnd = "2016-09-30" })).Select(d => d.NaturalKey));
        Assert.Equal(["auto"], (await h.Queries.ListByGroupAsync(Key("2026-10-02") with { MatchPeriod = true })).Select(d => d.NaturalKey));
        Assert.Equal(2, (await h.Queries.ListByGroupAsync(manual)).Count);   // sem o período, o modal não filtra por ele
    }

    [Fact]
    public async Task Modal_without_filters_lists_the_whole_execution_day_of_the_establishment_as_before()
    {
        using var h = await Harness.WithAsync(
            Row("nfe", "2026-08-07", "55", IntegrationStatus.IntegrationError),
            Row("nfse", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse, trigger: "Manual", periodStart: "2026-08-01", periodEnd: "2026-08-07"));

        Assert.Equal(2, (await h.Queries.ListByGroupAsync(Key("2026-08-07"))).Count);
    }

    [Fact]
    public async Task Modal_count_matches_the_row_total()
    {
        using var h = await Harness.WithAsync(
            Row("se-1", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse),
            Row("se-2", "2026-08-07", "SE", IntegrationStatus.Ignored, DocumentType.ServiceNfse),
            Row("se-3", "2026-08-07", "SE", IntegrationStatus.IntegrationError, DocumentType.ServiceNfse),
            Row("se-4", "2026-08-07", "SE", IntegrationStatus.Confirmed, DocumentType.ServiceNfse, trigger: "Manual", periodStart: "2026-08-01", periodEnd: "2026-08-07"),
            Row("nfe", "2026-08-07", "55", IntegrationStatus.Confirmed));

        DocumentGroup row = Assert.Single(await h.Queries.ListGroupsAsync(50), g => g.Model == "SE" && g.Trigger == "Automatic");
        IReadOnlyList<DocumentSummary> listed = await h.Queries.ListByGroupAsync(KeyOf(row));
        DocumentGroup manual = Assert.Single(await h.Queries.ListGroupsAsync(50), g => g.Model == "SE" && g.Trigger == "Manual");

        Assert.Equal(3, row.Total);
        Assert.Equal(row.Total, listed.Count);   // as ignoradas incluídas
        Assert.Equal(manual.Total, (await h.Queries.ListByGroupAsync(KeyOf(manual))).Count);
        Assert.Equal(
            row.Total + manual.Total,
            Assert.Single(await h.Queries.CountByModelAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7)), t => t.Model == "SE").Total);
    }

    // ---------- a contagem de reprocessos (conferência na tela, 2026-10-02) ----------

    [Fact]
    public async Task Each_reprocess_adds_one_and_the_modal_and_the_list_return_the_count()
    {
        using var h = await Harness.WithAsync(Row("nfe", "2026-08-07", "55", IntegrationStatus.IntegrationError));
        var log = new SqlReprocessLog(h.Db);

        await log.RecordAsync("tenant-a", "nfe");
        await log.RecordAsync("tenant-a", "nfe");

        Assert.Equal(2, Assert.Single(await h.Queries.ListByGroupAsync(Key("2026-08-07"))).Reprocessings);
        Assert.Equal(2, Assert.Single(await h.Queries.ListRecentAsync(10)).Reprocessings);
    }

    [Fact]
    public async Task Reprocess_count_starts_at_zero_and_does_not_touch_another_tenant()
    {
        using var h = await Harness.WithAsync(
            Row("nfe", "2026-08-07", "55", IntegrationStatus.IntegrationError),
            Row("nfe", "2026-08-07", "55", IntegrationStatus.IntegrationError, tenant: "tenant-b"));
        var log = new SqlReprocessLog(h.Db);

        await log.RecordAsync("tenant-b", "nfe");
        await log.RecordAsync("tenant-a", "sem-registro");   // nota sem registro: nada a contar, e sem falha

        Assert.Equal(0, Assert.Single(await h.Queries.ListRecentAsync(10)).Reprocessings);   // a do tenant-a
        Assert.Equal(1, await h.Db.ProcessedDocuments.Where(d => d.TenantId == "tenant-b").Select(d => d.ReprocessCount).SingleAsync());
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
        string groups = queries.GroupsQuery(200, new GroupWindow(new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 5), "55")).ToQueryString();

        Assert.Contains("GROUP BY", totals);
        Assert.Contains("[ExecutedOn] >= ", totals);
        Assert.Contains("GROUP BY", groups);
        Assert.Contains("[ExecutedOn] >= ", groups);
        Assert.Contains("[PeriodStart]", groups);
        Assert.Contains("[DocumentModel]", groups);
    }

    // ---------- apoio ----------

    // O dia da linha é o da execução; a data fiscal fica num dia que nenhum teste pede, para a contagem não poder usá-la.
    private static ProcessedDocument Row(
        string key, string executedOn, string model, IntegrationStatus status, DocumentType type = DocumentType.GoodsInvoice55,
        string tenant = "tenant-a", string company = Sp01, string branch = "SP-01", string? trigger = "Automatic",
        string? periodStart = null, string? periodEnd = null, string fiscalDate = "2001-01-01") => new()
    {
        TenantId = tenant,
        NaturalKey = key,
        Type = type,
        Status = status,
        CompanyCode = company,
        BranchCode = branch,
        ReferenceDate = fiscalDate,
        ExecutedOn = executedOn,
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        DocumentModel = model,
        Trigger = trigger,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static GroupKey Key(
        string executedOn, DocumentType? type = null, string? model = null, string? trigger = null, string company = Sp01) => new()
    {
        CompanyCode = company,
        BranchCode = "SP-01",
        ExecutedOn = executedOn,
        Type = type,
        Model = model,
        Trigger = trigger,
    };

    // A chave que o dashboard monta a partir da linha da tabela.
    private static GroupKey KeyOf(DocumentGroup row) => new()
    {
        CompanyCode = row.CompanyCode,
        BranchCode = row.BranchCode,
        ExecutedOn = row.ExecutedOn,
        Type = row.Type,
        Model = row.Model,
        Trigger = row.Trigger,
        MatchPeriod = true,
        PeriodStart = row.PeriodStart,
        PeriodEnd = row.PeriodEnd,
    };

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn) : IDisposable
    {
        public ProcessingDbContext Db => db;

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
