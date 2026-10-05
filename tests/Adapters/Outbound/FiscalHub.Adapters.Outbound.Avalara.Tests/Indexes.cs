using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// O índice da plataforma pelo caminho de produção — o resolvedor do núcleo sobre uma listagem fixa —, para os testes do
/// adapter que precisam dele sem HTTP. O índice não tem construtor público: quem o monta é só o resolvedor.
/// </summary>
internal static class Indexes
{
    public static Task<PlatformEstablishmentIndex> Of(params PlatformEstablishment[] establishments)
        => new PlatformEstablishmentResolver([new FixedListing(establishments)], new PlatformEstablishmentOptions(), TimeProvider.System)
            .GetAsync(new TenantConnectorProfile { TenantId = "tenant-a", Environment = "Sandbox", InboundAdapter = "Xml", OutboundAdapter = "Avalara" });

    private sealed class FixedListing(PlatformEstablishment[] establishments) : IPlatformEstablishmentListing
    {
        public string Adapter => "Avalara";

        public Task<IReadOnlyList<PlatformEstablishment>> ListAsync(TenantConnectorProfile profile, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlatformEstablishment>>(establishments);
    }
}
