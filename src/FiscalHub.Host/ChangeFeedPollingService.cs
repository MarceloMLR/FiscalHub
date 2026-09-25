using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Host;

/// <summary>
/// Casca de infraestrutura do feed de mudanças (ADR-0024): um timer que, a cada tick, abre um escopo e
/// chama <see cref="ChangeFeedPoller.RunOnceAsync"/>. O intervalo por tenant (padrão 60s) é decidido
/// pelo poller; o tick só dá a resolução. A lógica vive no poller (testável); aqui é só o timer.
/// </summary>
internal sealed class ChangeFeedPollingService(IServiceProvider services, ILogger<ChangeFeedPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                ChangeFeedPoller poller = scope.ServiceProvider.GetRequiredService<ChangeFeedPoller>();

                Report(await poller.RunOnceAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo do feed de mudanças.");
            }
        }
    }

    private void Report(ChangeFeedPassSummary summary)
    {
        if (summary.TenantsPolled > 0)
        {
            logger.LogInformation(
                "Feed de mudanças: {Tenants} tenant(s) consultado(s), {Enqueued} referência(s) na fila de descoberta.",
                summary.TenantsPolled, summary.ReferencesEnqueued);
        }

        foreach ((string tenant, string error) in summary.Failures)
        {
            logger.LogWarning("Feed de mudanças: poll do tenant {Tenant} falhou; a marca d'água não avançou. {Error}", tenant, error);
        }

        foreach (string tenant in summary.LeasesLost)
        {
            logger.LogWarning("Feed de mudanças: lease do tenant {Tenant} perdido no meio do poll; outra réplica segue.", tenant);
        }

        foreach (string tenant in summary.Stalled)
        {
            logger.LogWarning(
                "Feed de mudanças: tenant {Tenant} parou no teto de páginas sem sair da janela de sobreposição. Se repetir, aumente o pageSize.",
                tenant);
        }
    }
}
