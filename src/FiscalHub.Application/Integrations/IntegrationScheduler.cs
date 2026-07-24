namespace FiscalHub.Application.Integrations;

/// <summary>
/// Núcleo do agendador: a cada passada, pega os agendamentos vencidos, calcula o período de cada um
/// (D-1 no diário; explícito no único), dispara pelo runner e reprograma (diário: +1 dia; único:
/// desativa). Lógica pura — um BackgroundService só chama <see cref="RunDueAsync"/> num timer.
/// </summary>
public sealed class IntegrationScheduler
{
    // Período fiscal é dia cheio em horário de Brasília.
    private static readonly TimeSpan Brt = TimeSpan.FromHours(-3);

    private readonly IScheduleStore _schedules;
    private readonly IIntegrationRunner _runner;
    private readonly TimeProvider _clock;

    public IntegrationScheduler(IScheduleStore schedules, IIntegrationRunner runner, TimeProvider clock)
    {
        _schedules = schedules;
        _runner = runner;
        _clock = clock;
    }

    /// <summary>Executa os agendamentos vencidos. Devolve quantos rodaram.</summary>
    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        IReadOnlyList<ScheduledIntegration> due = await _schedules.ListDueAsync(now, ct);

        foreach (ScheduledIntegration schedule in due)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                (DateTimeOffset start, DateTimeOffset end) = ComputePeriod(schedule);

                await _runner.RunAsync(new RunRequest
                {
                    Mode = schedule.Mode,
                    TenantId = schedule.TenantId,
                    CompanyCode = schedule.CompanyCode,
                    BranchCode = schedule.BranchCode,
                    PeriodStart = start,
                    PeriodEnd = end,
                    ScheduleId = schedule.Id,
                }, ct);

                // Diário reprograma pro próximo dia; único desativa (nextRunAt null).
                DateTimeOffset? next = schedule.Mode == IntegrationMode.ScheduledDaily
                    ? schedule.NextRunAt.AddDays(1)
                    : null;
                await _schedules.RescheduleAsync(schedule.Id, next, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Um agendamento com problema não derruba os outros nem trava o ciclo. Como o
                // NextRunAt não avançou, ele é retentado na próxima passada.
            }
        }

        return due.Count;
    }

    /// <summary>D-1: o dia anterior à data do disparo (dia cheio, BRT). Único: o período explícito.</summary>
    internal static (DateTimeOffset Start, DateTimeOffset End) ComputePeriod(ScheduledIntegration schedule)
    {
        if (schedule.Mode == IntegrationMode.ScheduledOnce)
        {
            DateOnly start = DateOnly.ParseExact(schedule.PeriodStart!, "yyyy-MM-dd");
            DateOnly end = DateOnly.ParseExact(schedule.PeriodEnd!, "yyyy-MM-dd");
            return (DayStart(start), DayEnd(end));
        }

        DateOnly yesterday = DateOnly.FromDateTime(schedule.NextRunAt.ToOffset(Brt).Date).AddDays(-1);
        return (DayStart(yesterday), DayEnd(yesterday));
    }

    private static DateTimeOffset DayStart(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), Brt);

    private static DateTimeOffset DayEnd(DateOnly d) => new(d.ToDateTime(new TimeOnly(23, 59, 59)), Brt);
}
