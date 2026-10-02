namespace FiscalHub.Application.Integrations;

/// <summary>
/// O dia de uma execução, em Brasília: o dia da linha da nota no dashboard (change erp-company-directory-and-card-filters,
/// D15). É o mesmo fuso fixo do agendador, que calcula o D-1 e os disparos em horário de Brasília.
/// </summary>
public static class ExecutionDay
{
    public static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public static DateOnly Of(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(Brasilia).DateTime);
}
