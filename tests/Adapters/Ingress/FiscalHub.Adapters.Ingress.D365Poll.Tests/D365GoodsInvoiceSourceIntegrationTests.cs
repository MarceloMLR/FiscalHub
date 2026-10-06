using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Monta as NF-e modelo 55 de um F&amp;O REAL. Opt-in, no mesmo padrão do teste do feed: só roda com a URL e a credencial
/// do app do conector definidas (<see cref="D365IntegrationEnvironment"/>), e autentica como o conector, por client
/// credentials; <c>FISCALHUB_D365_COMPANY</c> escolhe a empresa (padrão brmf). Sem as variáveis, aparece como pulado,
/// nomeando o que falta, e não toca a rede.
/// </summary>
public class D365GoodsInvoiceSourceIntegrationTests
{
    [D365IntegrationFact]
    public async Task Every_goods_invoice_of_the_company_assembles_and_twice_gives_the_same_fingerprint()
    {
        string url = D365IntegrationEnvironment.Read(D365IntegrationEnvironment.Url);
        string company = Environment.GetEnvironmentVariable("FISCALHUB_D365_COMPANY") is { Length: > 0 } c ? c : "brmf";
        ClientCredentialsD365TokenProvider tokens = D365IntegrationEnvironment.Tokens();
        var profiles = new D365ChangeFeedTests.FakeProfiles
        {
            Profile = new TenantConnectorProfile
            {
                TenantId = "tenant-a",
                Environment = "Sandbox",
                InboundAdapter = "Dynamics365",
                InboundSettings = $$"""{"url":"{{url}}","companies":["{{company}}"],"auth":{{D365IntegrationEnvironment.AuthJson()}}}""",
                OutboundAdapter = "Avalara",
            },
        };
        var source = new D365GoodsInvoiceSource(
            new HttpClient(), profiles, tokens, new D365ChangeFeedOptions(),
            new D365ReferenceDataCache(new D365AssemblyOptions(), TimeProvider.System), new NoOpProcessingTrace(),
            TimeProvider.System, NullLogger<D365GoodsInvoiceSource>.Instance);

        // Quais são as notas 55 da empresa: Model é string, não enum — o filtro vai direto.
        var client = new D365ODataClient(new HttpClient(), tokens, new D365ChangeFeedOptions(), TimeProvider.System);
        IReadOnlyList<JsonElement> headers = await client.GetAllAsync(
            new Uri($"{url.TrimEnd('/')}/data/FSFiscalDocumentBRs?cross-company=true&$select=FiscalDocumentRecId,dataAreaId,Voucher&$filter="
                + Uri.EscapeDataString($"dataAreaId eq '{company}' and Model eq '55'")),
            new D365Connection("tenant-a", new Uri(url), Auth: null), default);

        Assert.NotEmpty(headers);
        foreach (JsonElement header in headers)
        {
            var reference = new DocumentReference
            {
                TenantId = "tenant-a",
                Type = DocumentType.GoodsInvoice55,
                NaturalKey = $"{company}|{header.GetProperty("Voucher").GetString()}",
                Locator = $"d365/{company}/{header.GetProperty("FiscalDocumentRecId").GetInt64()}",
                Origin = "Dynamics365",
            };

            FetchResult<GoodsInvoice> first = await source.FetchAsync(reference);
            FetchResult<GoodsInvoice> second = await source.FetchAsync(reference);

            Assert.NotEmpty(first.Document.Items);
            Assert.Equal(first.ContentHash, second.ContentHash);   // nada mudou → mesma impressão
        }
    }
}
