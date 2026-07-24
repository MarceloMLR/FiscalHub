namespace FiscalHub.Application.Integrations;

/// <summary>
/// Como uma execução de integração foi disparada. É o "modo" que o painel mostra na coluna Tipo.
/// (O tempo-real por evento não gera execução — ele aparece nas notas do dia; execução é só pra
/// disparo manual ou agendado.)
/// </summary>
public enum IntegrationMode
{
    /// <summary>Disparo manual pelo usuário (tela de integração manual).</summary>
    Manual,

    /// <summary>Agendamento recorrente diário processando o dia anterior (D-1).</summary>
    ScheduledDaily,

    /// <summary>Agendamento único marcado para uma data/hora futura.</summary>
    ScheduledOnce,
}
