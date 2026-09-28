using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Registro dos pares (documento, carimbo) já publicados pelo poller (design D16): a chave inclui o
/// carimbo, é isolado por (tenant, origem), podado pela janela e zerado quando a marca regride.
/// </summary>
public class ChangeFeedPublicationLogTests
{
    private const string Origin = "Dynamics365";

    [Fact]
    public void Recorded_pair_is_recognized()
    {
        var log = new ChangeFeedPublicationLog();

        log.Record("tenant-a", Origin, "brmf|A", At(11, 58));

        Assert.True(log.WasPublished("tenant-a", Origin, "brmf|A", At(11, 58)));
    }

    [Fact]
    public void Same_document_with_another_stamp_is_not_recognized()
    {
        var log = new ChangeFeedPublicationLog();
        log.Record("tenant-a", Origin, "brmf|A", At(11, 58));

        // Nota alterada de verdade: carimbo novo, precisa ser republicada.
        Assert.False(log.WasPublished("tenant-a", Origin, "brmf|A", At(11, 59, 30)));
    }

    [Fact]
    public void Pairs_are_isolated_by_tenant_and_origin()
    {
        var log = new ChangeFeedPublicationLog();
        log.Record("tenant-a", Origin, "brmf|A", At(11, 58));

        Assert.False(log.WasPublished("tenant-c", Origin, "brmf|A", At(11, 58)));
        Assert.False(log.WasPublished("tenant-a", "iScala", "brmf|A", At(11, 58)));
    }

    [Fact]
    public void Begin_pull_prunes_only_pairs_at_or_before_since()
    {
        var log = new ChangeFeedPublicationLog();
        log.Record("tenant-a", Origin, "brmf|old", At(11, 50));
        log.Record("tenant-a", Origin, "brmf|edge", At(11, 55));
        log.Record("tenant-a", Origin, "brmf|new", At(11, 58));

        log.BeginPull("tenant-a", Origin, watermark: At(12, 0), since: At(11, 55));

        Assert.False(log.WasPublished("tenant-a", Origin, "brmf|old", At(11, 50)));
        Assert.False(log.WasPublished("tenant-a", Origin, "brmf|edge", At(11, 55)));   // "gt since" não o devolve mais
        Assert.True(log.WasPublished("tenant-a", Origin, "brmf|new", At(11, 58)));
    }

    [Fact]
    public void Watermark_going_back_clears_only_that_tenant_and_origin()
    {
        var log = new ChangeFeedPublicationLog();
        log.BeginPull("tenant-a", Origin, watermark: At(12, 0), since: At(11, 55));
        log.Record("tenant-a", Origin, "brmf|A", At(11, 58));
        log.Record("tenant-c", Origin, "brmf|A", At(11, 58));

        // Rebobinamento: quem rebobina quer tudo de volta na fila.
        log.BeginPull("tenant-a", Origin, watermark: At(8, 0), since: At(7, 55));

        Assert.False(log.WasPublished("tenant-a", Origin, "brmf|A", At(11, 58)));
        Assert.True(log.WasPublished("tenant-c", Origin, "brmf|A", At(11, 58)));
    }

    [Fact]
    public void Forget_clears_pairs_and_last_watermark_of_only_that_tenant_and_origin()
    {
        var log = new ChangeFeedPublicationLog();
        log.BeginPull("tenant-a", Origin, watermark: At(12, 0), since: At(11, 55));
        log.Advanced("tenant-a", Origin, At(12, 5));
        log.Record("tenant-a", Origin, "brmf|A", At(11, 58));
        log.Record("tenant-c", Origin, "brmf|A", At(11, 58));

        log.Forget("tenant-a", Origin);

        Assert.False(log.WasPublished("tenant-a", Origin, "brmf|A", At(11, 58)));
        Assert.True(log.WasPublished("tenant-c", Origin, "brmf|A", At(11, 58)));

        // A última marca vista também foi esquecida: a leitura seguinte é a de uma partição nova, e não um rebobinamento.
        log.Record("tenant-a", Origin, "brmf|B", At(8, 30));
        log.BeginPull("tenant-a", Origin, watermark: At(8, 0), since: At(7, 55));
        Assert.True(log.WasPublished("tenant-a", Origin, "brmf|B", At(8, 30)));
    }

    [Fact]
    public void Rewind_below_the_last_advanced_watermark_is_detected()
    {
        var log = new ChangeFeedPublicationLog();
        log.BeginPull("tenant-a", Origin, watermark: At(12, 0), since: At(11, 55));
        log.Record("tenant-a", Origin, "brmf|A", At(12, 3));
        log.Advanced("tenant-a", Origin, At(12, 5));

        // Marca rebobinada para um ponto entre o início da leitura anterior e o último avanço.
        log.BeginPull("tenant-a", Origin, watermark: At(12, 2), since: At(11, 57));

        Assert.False(log.WasPublished("tenant-a", Origin, "brmf|A", At(12, 3)));
    }

    [Fact]
    public void Watermark_moving_forward_keeps_the_pairs_in_the_window()
    {
        var log = new ChangeFeedPublicationLog();
        log.BeginPull("tenant-a", Origin, watermark: At(12, 0), since: At(11, 55));
        log.Record("tenant-a", Origin, "brmf|A", At(11, 58));
        log.Advanced("tenant-a", Origin, At(12, 1));

        log.BeginPull("tenant-a", Origin, watermark: At(12, 1), since: At(11, 56));

        Assert.True(log.WasPublished("tenant-a", Origin, "brmf|A", At(11, 58)));
    }

    [Fact]
    public void Concurrent_records_are_not_lost()
    {
        var log = new ChangeFeedPublicationLog();

        Parallel.For(0, 2000, i => log.Record($"tenant-{i % 4}", Origin, $"brmf|{i}", At(11, 58)));

        Assert.All(Enumerable.Range(0, 2000), i => Assert.True(log.WasPublished($"tenant-{i % 4}", Origin, $"brmf|{i}", At(11, 58))));
    }

    private static DateTimeOffset At(int hour, int minute, int second = 0)
        => new(2026, 9, 25, hour, minute, second, TimeSpan.Zero);
}
