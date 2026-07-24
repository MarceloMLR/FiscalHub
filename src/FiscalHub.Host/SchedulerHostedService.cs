using FiscalHub.Application.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Host;

/// <summary>
/// Casca de infraestrutura do agendador: um timer que, a cada intervalo, abre um escopo e chama
/// <see cref="IntegrationScheduler.RunDueAsync"/>. A lógica vive no scheduler (testável); aqui é só
/// o timer. No cloud, isto vira um timer trigger de Functions.
/// </summary>
internal sealed class SchedulerHostedService(IServiceProvider services, ILogger<SchedulerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                IntegrationScheduler scheduler = scope.ServiceProvider.GetRequiredService<IntegrationScheduler>();

                int ran = await scheduler.RunDueAsync(stoppingToken);
                if (ran > 0)
                {
                    logger.LogInformation("Agendador: {Count} agendamento(s) executado(s).", ran);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo do agendador.");
            }
        }
    }
}
