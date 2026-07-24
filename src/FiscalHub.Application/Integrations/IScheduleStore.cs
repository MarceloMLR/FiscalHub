namespace FiscalHub.Application.Integrations;

/// <summary>Persiste os agendamentos de integração. Implementada na Infrastructure.</summary>
public interface IScheduleStore
{
    Task<int> CreateAsync(ScheduledIntegration schedule, CancellationToken ct = default);

    Task<IReadOnlyList<ScheduledIntegration>> ListAsync(CancellationToken ct = default);

    /// <summary>Agendamentos ativos cujo horário já venceu (<c>NextRunAt &lt;= now</c>).</summary>
    Task<IReadOnlyList<ScheduledIntegration>> ListDueAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Reprograma o próximo disparo; <paramref name="nextRunAt"/> <c>null</c> desativa (caso único).</summary>
    Task RescheduleAsync(int id, DateTimeOffset? nextRunAt, CancellationToken ct = default);

    Task DeactivateAsync(int id, CancellationToken ct = default);
}
