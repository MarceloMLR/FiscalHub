using System.Net;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica o adapter de despacho: envio (mapeia → camelCase → POST → recibo), tradução do
/// status nativo para <see cref="IntegrationStatus"/>, e o gancho de token. HttpClient é falso
/// (HttpMessageHandler stub) — sem libs de mock.
/// </summary>
public class AvalaraComplianceDispatcherTests
{
    [Fact]
    public async Task Submit_maps_posts_camelCase_and_returns_receipt()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var dispatcher = Build(handler);

        IntegrationReceipt receipt = await dispatcher.SubmitAsync(SampleInvoice(), Context());

        Assert.Equal("ext-guid-1", receipt.ExternalId);
        Assert.Equal(IntegrationStatus.Submitted, receipt.Status);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://localhost/documents", handler.LastRequest.RequestUri!.ToString());
        // serialização camelCase (não PascalCase) e nada de status nativo no payload de envio
        Assert.Contains("\"chaveNFe\"", handler.LastRequestBody);
        Assert.Contains("\"itens\"", handler.LastRequestBody);
        Assert.DoesNotContain("ChaveNFe", handler.LastRequestBody);
    }

    [Theory]
    [InlineData("carregado", IntegrationStatus.Confirmed)]
    [InlineData("erro", IntegrationStatus.IntegrationError)]
    [InlineData("processando", IntegrationStatus.Submitted)]
    [InlineData("qualquer-outro", IntegrationStatus.Submitted)]
    public async Task CheckStatus_translates_native_status(string native, IntegrationStatus expected)
    {
        var handler = new StubHttpMessageHandler($$"""{"id":"ext-guid-1","status":"{{native}}"}""");
        var dispatcher = Build(handler);

        IntegrationResult result = await dispatcher.CheckStatusAsync("ext-guid-1", Context());

        Assert.Equal(expected, result.Status);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("http://localhost/documents/ext-guid-1/status", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task CheckStatus_error_sets_platform_agnostic_message_without_native_status()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1","status":"erro"}""");
        var dispatcher = Build(handler);

        IntegrationResult result = await dispatcher.CheckStatusAsync("ext-guid-1", Context());

        Assert.Equal(IntegrationStatus.IntegrationError, result.Status);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("erro", result.Message); // não vaza o status nativo
    }

    [Fact]
    public async Task CheckStatus_treats_204_no_content_as_still_pending()
    {
        // A Avalara devolve 204 (sem corpo) quando a nota ainda não foi processada.
        var handler = new StubHttpMessageHandler(string.Empty, HttpStatusCode.NoContent);
        var dispatcher = Build(handler);

        IntegrationResult result = await dispatcher.CheckStatusAsync("ext-guid-1", Context());

        Assert.Equal(IntegrationStatus.Submitted, result.Status);
    }

    [Fact]
    public async Task Submit_applies_bearer_token_when_provider_returns_one()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var dispatcher = Build(handler, new FakeTokenProvider("tok-123"));

        await dispatcher.SubmitAsync(SampleInvoice(), Context());

        var auth = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("tok-123", auth.Parameter);
    }

    [Fact]
    public async Task Submit_sends_no_authorization_header_with_noop_token()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var dispatcher = Build(handler); // NoOp token provider

        await dispatcher.SubmitAsync(SampleInvoice(), Context());

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task Submit_propagates_on_non_success_status()
    {
        var handler = new StubHttpMessageHandler("""{"error":"boom"}""", HttpStatusCode.InternalServerError);
        var dispatcher = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));
    }

    [Fact]
    public async Task Submit_throws_clean_exception_when_response_has_no_id()
    {
        var handler = new StubHttpMessageHandler("""{}"""); // 200 sem "id"
        var dispatcher = Build(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));
    }

    [Fact]
    public async Task Submit_throws_clean_exception_on_empty_body()
    {
        var handler = new StubHttpMessageHandler(""); // 200 com corpo vazio: não pode vazar JsonException
        var dispatcher = Build(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));
    }

    [Fact]
    public async Task CheckStatus_propagates_on_non_success_status()
    {
        var handler = new StubHttpMessageHandler("""{"error":"boom"}""", HttpStatusCode.InternalServerError);
        var dispatcher = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.CheckStatusAsync("ext-guid-1", Context()));
    }

    private static AvalaraComplianceDispatcher Build(StubHttpMessageHandler handler, IAvalaraTokenProvider? token = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var options = Options.Create(new AvalaraOptions { BaseUrl = "http://localhost/", Destination = "avalara" });
        return new AvalaraComplianceDispatcher(http, options, token ?? new NoOpAvalaraTokenProvider());
    }

    private sealed class FakeTokenProvider(string token) : IAvalaraTokenProvider
    {
        public Task<string> GetTokenAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    private static DispatchContext Context() => new()
    {
        TenantId = "tenant-a",
        CorrelationId = "corr-1",
        Operation = DocumentStatus.Issued,
    };

    private static GoodsInvoice SampleInvoice() => new()
    {
        AccessKey = "35260612345678000190550010000001231000000123",
        Model = "55",
        Series = "1",
        Number = "123",
        IssueDate = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(-3)),
        Issuer = new Party { TaxId = "12345678000190", Name = "Emitente LTDA" },
        Recipient = new Party { TaxId = "98765432000110", Name = "Cliente SA" },
        TotalAmount = 100.00m,
        Items =
        [
            new GoodsInvoiceItem
            {
                Number = 1,
                ProductCode = "PROD-001",
                Description = "Produto de Teste",
                Ncm = "12345678",
                Cfop = "5102",
                Quantity = 2m,
                UnitAmount = 50m,
                TotalAmount = 100m,
                ReformTaxes = new ReformTaxes
                {
                    Cst = "000",
                    ClassTrib = "000001",
                    TaxBase = 100m,
                    IbsCbs = new IbsCbs
                    {
                        IbsState = new TaxShare { Rate = 8.50m, Amount = 8.50m },
                        IbsMunicipality = new TaxShare { Rate = 2.00m, Amount = 2.00m },
                        IbsTotalAmount = 10.50m,
                        Cbs = new TaxShare { Rate = 0.90m, Amount = 0.90m },
                    },
                },
            },
        ],
    };
}
