using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Host;

/// <summary>
/// Casca de infraestrutura do poll: um timer que, a cada intervalo, abre um escopo e chama
/// <see cref="StatusPoller{TDocument}.PollOnceAsync"/>. A lógica vive no poller (testável); aqui é
/// só o agendamento. Na Etapa de Service Bus isto pode virar uma function agendada.
/// </summary>
internal sealed class StatusPollingService(IServiceProvider services, ILogger<StatusPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                StatusPoller<GoodsInvoice> poller = scope.ServiceProvider.GetRequiredService<StatusPoller<GoodsInvoice>>();

                int polled = await poller.PollOnceAsync(stoppingToken);
                if (polled > 0)
                {
                    logger.LogInformation("Poll de status: {Count} documento(s) consultado(s).", polled);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo de poll de status.");
            }
        }
    }
}
