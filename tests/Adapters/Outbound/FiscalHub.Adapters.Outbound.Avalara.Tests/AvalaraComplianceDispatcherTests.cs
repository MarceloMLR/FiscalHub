using System.Net;
using System.Text.Json.Nodes;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica o adapter de despacho: envio (mapeia → camelCase → POST → recibo), tradução do
/// status nativo para <see cref="IntegrationStatus"/>, e o token do tenant em cada requisição (ADR-0027). HttpClient é
/// falso (HttpMessageHandler stub) — sem libs de mock.
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

    [Fact]
    public async Task Submit_uses_base_url_from_tenant_profile()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var profiles = new FakeProfileStore(new TenantConnectorProfile
        {
            TenantId = "tenant-a",
            Environment = "Sandbox",
            InboundAdapter = "Dynamics365",
            OutboundAdapter = "Avalara",
            OutboundSettings = """
                {"sandbox":{"baseUrl":"https://avalara-a/","clientId":"id-a","clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret",
                            "establishments":{"12345678000190":{"codigoEmpresa":"E","codigoContribuinte":"C"}}},
                 "production":{"baseUrl":"https://avalara-a-prod/"}}
                """,
        });
        var dispatcher = Build(handler, profiles: profiles);

        await dispatcher.SubmitAsync(SampleInvoice(), Context());

        // A base veio do perfil do tenant (ambiente sandbox): não há URL global do adapter.
        Assert.Equal("https://avalara-a/documents", handler.LastRequest!.RequestUri!.ToString());
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
    public async Task CheckStatus_error_carries_the_platform_reason()
    {
        // O motivo da plataforma atravessa como texto; o status segue normalizado (ADR-0003, refinado pelo ADR-0026).
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1","status":"erro","mensagens":["CFOP 1556 incompatível com a operação"]}""");
        var dispatcher = Build(handler);

        IntegrationResult result = await dispatcher.CheckStatusAsync("ext-guid-1", Context());

        Assert.Equal(IntegrationStatus.IntegrationError, result.Status);
        Assert.Equal("Plataforma de compliance rejeitou: CFOP 1556 incompatível com a operação", result.Message);
    }

    [Fact]
    public async Task CheckStatus_error_without_message_says_the_platform_gave_no_reason_without_leaking_the_native_status()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1","status":"erro"}""");
        var dispatcher = Build(handler);

        IntegrationResult result = await dispatcher.CheckStatusAsync("ext-guid-1", Context());

        Assert.Equal(IntegrationStatus.IntegrationError, result.Status);
        Assert.Equal("Plataforma de compliance rejeitou sem informar a causa.", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Submit_content_refusal_is_a_rejection_with_the_platform_reason_after_a_single_request(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler("""{"mensagens":["codigoEmpresa não cadastrado"]}""", status);
        var dispatcher = Build(handler);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.Equal("Plataforma de compliance recusou: codigoEmpresa não cadastrado", ex.Reason);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Submit_refusal_with_empty_body_cites_the_http_status()
    {
        var handler = new StubHttpMessageHandler("", HttpStatusCode.UnprocessableEntity);
        var dispatcher = Build(handler);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.Contains("HTTP 422", ex.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Submit_other_failures_still_propagate_for_the_native_retry(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler("""{"mensagens":["indisponível"]}""", status);
        var dispatcher = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"message":"no Route matched with those values"}""")]
    public async Task Not_found_on_submit_is_a_configuration_rejection_naming_both_parts_of_the_url(string body)
    {
        // O caminho de envio do sandbox vem da URL do cliente e só é exercitado no teste manual: se estiver errado, o
        // motivo tem de dizer onde corrigir, e não virar retentativa até a dead-letter.
        var handler = new StubHttpMessageHandler(body, HttpStatusCode.NotFound);
        var trace = new RecordingTrace();
        var dispatcher = Build(handler, trace: trace, documentsPath: "taxcompliance/v2/fiscal/dfe");

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.StartsWith("Configuração do conector:", ex.Reason);
        Assert.Contains("o caminho de envio não existe nessa URL", ex.Reason);
        Assert.Contains("HTTP 404 em POST http://localhost/taxcompliance/v2/fiscal/dfe", ex.Reason);
        Assert.Contains("OutboundSettings.sandbox.baseUrl", ex.Reason);                           // o host, no perfil
        Assert.Contains("Configurações → Conectores → Avalara → Sandbox → URL base", ex.Reason);  // na tela
        Assert.Contains("Avalara:DocumentsPath", ex.Reason);                                      // o caminho, no appsettings
        Assert.Contains("taxcompliance/v2/fiscal/dfe", ex.Reason);
        if (body.Length > 0)
        {
            Assert.Contains("no Route matched with those values", ex.Reason);
        }

        Assert.Equal(1, handler.RequestCount);                                                    // sem retentativa
        Assert.True(trace.Responses.ContainsKey(TraceExchanges.Submit));                          // e a resposta fotografada
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
    public async Task CheckStatus_treats_404_not_found_as_still_pending()
    {
        // 404 = a plataforma ainda não conhece o identificador → pendente, não estoura o poll do lote.
        var handler = new StubHttpMessageHandler(string.Empty, HttpStatusCode.NotFound);
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
        var dispatcher = Build(handler, new NoOpAvalaraTokenProvider());   // só por pedido explícito

        await dispatcher.SubmitAsync(SampleInvoice(), Context());

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task Submit_traces_outbound_payload_before_send()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var trace = new RecordingTrace();
        var dispatcher = Build(handler, trace: trace);

        await dispatcher.SubmitAsync(SampleInvoice(), Context());

        // O dispatcher fotografa só o destino; o domínio é responsabilidade da esteira.
        Assert.Null(trace.Domain);
        Assert.NotNull(trace.Outbound);
        Assert.Equal("avalara", trace.Outbound!.Value.Destination);
        Assert.Contains("\"chaveNFe\"", trace.Outbound.Value.Json);
    }

    [Fact]
    public async Task Submit_propagates_on_non_success_status()
    {
        var handler = new StubHttpMessageHandler("""{"error":"boom"}""", HttpStatusCode.InternalServerError);
        var dispatcher = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));
    }

    // ---------- autenticação e desfecho (ADR-0027) ----------

    [Fact]
    public async Task Each_send_carries_the_token_of_its_own_tenant()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var tokens = new FakeTokenProvider(tenant => $"tok-de-{tenant}");
        var dispatcher = Build(handler, tokens);

        await dispatcher.SubmitAsync(SampleInvoice(), Context("tenant-a"));
        string? first = handler.LastRequest!.Headers.Authorization?.Parameter;
        await dispatcher.SubmitAsync(SampleInvoice(), Context("tenant-b"));
        string? second = handler.LastRequest!.Headers.Authorization?.Parameter;

        Assert.Equal("tok-de-tenant-a", first);
        Assert.Equal("tok-de-tenant-b", second);
        Assert.Equal(["tenant-a", "tenant-b"], tokens.Asked.Select(s => s.TenantId));
    }

    [Fact]
    public async Task Missing_secret_makes_no_request_at_all()
    {
        var documents = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var tokenEndpoint = new AvalaraTokenProviderTests.TokenEndpointStub();
        var provider = new AvalaraTokenProvider(new HttpClient(tokenEndpoint), new AvalaraTokenProviderTests.FakeSecrets(),
            Options.Create(new AvalaraOptions()), TimeProvider.System, new CapturingLogger<AvalaraTokenProvider>());
        var dispatcher = Build(documents, provider);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.Contains("Configurações → Conectores → Avalara → Sandbox → Client Secret", ex.Reason);
        Assert.Equal(0, tokenEndpoint.Calls);
        Assert.Equal(0, documents.RequestCount);
    }

    [Fact]
    public async Task Forbidden_is_a_configuration_rejection_with_the_platform_reason_after_a_single_post()
    {
        var handler = new StubHttpMessageHandler("""{"message":"cliente sem acesso à empresa"}""", HttpStatusCode.Forbidden);
        var dispatcher = Build(handler);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.StartsWith("Configuração do conector:", ex.Reason);
        Assert.Contains("'tenant-a'", ex.Reason);
        Assert.Contains("'sandbox'", ex.Reason);
        Assert.Contains("HTTP 403", ex.Reason);
        Assert.Contains("cliente sem acesso à empresa", ex.Reason);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Unauthorized_with_a_fresh_token_is_a_configuration_rejection()
    {
        var handler = new StubHttpMessageHandler("""{"message":"token inválido"}""", HttpStatusCode.Unauthorized);
        var tokens = new FakeTokenProvider("tok-novo", fresh: true);
        var dispatcher = Build(handler, tokens);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.StartsWith("Configuração do conector:", ex.Reason);
        Assert.Contains("HTTP 401", ex.Reason);
        Assert.Contains("token inválido", ex.Reason);
        Assert.Empty(tokens.Invalidated);   // pedir outro não muda nada
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Unauthorized_with_a_cached_token_invalidates_it_and_the_next_attempt_asks_for_a_new_one()
    {
        var documents = new SequencedHandler(
            (HttpStatusCode.OK, """{"id":"ext-1"}"""),
            (HttpStatusCode.Unauthorized, """{"message":"token expirado"}"""),
            (HttpStatusCode.OK, """{"id":"ext-3"}"""));
        var tokenEndpoint = new AvalaraTokenProviderTests.TokenEndpointStub();
        var secrets = new AvalaraTokenProviderTests.FakeSecrets();
        secrets.Values["fh-tenant-a--outbound--sandbox--clientsecret"] = "segredo-a";
        var provider = new AvalaraTokenProvider(new HttpClient(tokenEndpoint), secrets,
            Options.Create(new AvalaraOptions()), TimeProvider.System, new CapturingLogger<AvalaraTokenProvider>());
        var dispatcher = Build(documents, provider);

        await dispatcher.SubmitAsync(SampleInvoice(), Context());                                                   // token novo
        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));  // do cache: 401
        IntegrationReceipt receipt = await dispatcher.SubmitAsync(SampleInvoice(), Context());                      // outro token

        Assert.Equal("ext-3", receipt.ExternalId);
        Assert.Equal(2, tokenEndpoint.Calls);
        Assert.Equal(["Bearer tok-1", "Bearer tok-1", "Bearer tok-2"], documents.Authorizations);
    }

    [Fact]
    public async Task Unauthorized_on_the_status_check_invalidates_the_token()
    {
        var handler = new StubHttpMessageHandler("""{"message":"token expirado"}""", HttpStatusCode.Unauthorized);
        var tokens = new FakeTokenProvider("tok-cache");
        var dispatcher = Build(handler, tokens);

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.CheckStatusAsync("ext-guid-1", Context()));

        Assert.Equal("tok-cache", Assert.Single(tokens.Invalidated).Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "")]
    [InlineData(HttpStatusCode.Created, "aceito")]
    [InlineData(HttpStatusCode.Created, """{"protocolo":"123"}""")]
    [InlineData(HttpStatusCode.OK, """{"id":""}""")]
    [InlineData(HttpStatusCode.OK, """["ext-1"]""")]
    public async Task Success_without_a_recognizable_id_is_a_rejection_that_is_not_resent(HttpStatusCode status, string body)
    {
        var handler = new StubHttpMessageHandler(body, status);
        var dispatcher = Build(handler);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.StartsWith("Conector:", ex.Reason);
        Assert.Contains($"HTTP {(int)status} com sucesso", ex.Reason);
        Assert.Contains("sem identificador", ex.Reason);
        Assert.Contains("pode ter sido aceito", ex.Reason);
        Assert.Contains("não será reenviado", ex.Reason);
        Assert.Equal(1, handler.RequestCount);
    }

    // ---------- a quarta foto: a resposta da plataforma (ADR-0027) ----------

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"id":"ext-1"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"mensagens":["codigoEmpresa não cadastrado"]}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"mensagens":["indisponível"]}""")]
    public async Task Submit_response_is_photographed_in_any_status(HttpStatusCode status, string body)
    {
        var handler = new HeaderedHandler(status, body);
        var trace = new RecordingTrace();
        var dispatcher = Build(handler, trace: trace);

        try
        {
            await dispatcher.SubmitAsync(SampleInvoice(), Context());
        }
        catch (Exception ex) when (ex is DispatchRejectedException or HttpRequestException)
        {
            // o desfecho é dos outros testes; aqui, só a foto
        }

        JsonNode photo = JsonNode.Parse(trace.Responses[TraceExchanges.Submit])!;
        Assert.Equal("submit", (string?)photo["exchange"]);
        Assert.Equal("POST", (string?)photo["request"]!["method"]);
        Assert.Equal("http://localhost/documents", (string?)photo["request"]!["url"]);
        Assert.Equal((int)status, (int?)photo["response"]!["status"]);
        Assert.Equal(Now, DateTimeOffset.Parse((string)photo["response"]!["receivedAt"]!));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(body), photo["response"]!["body"]));
        Assert.Equal(0, (int?)photo["redactions"]);

        JsonObject headers = photo["response"]!["headers"]!.AsObject();
        Assert.StartsWith("application/json", (string?)headers["Content-Type"]);
        Assert.Equal("corr-da-plataforma", (string?)headers["X-Correlation-Id"]);
        Assert.NotNull(headers["Date"]);
        Assert.DoesNotContain(headers, h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headers, h => h.Key.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase));
        Assert.Null(photo["request"]!["headers"]);   // nenhum cabeçalho de requisição
    }

    [Fact]
    public async Task Body_that_is_not_json_goes_as_text()
    {
        var trace = new RecordingTrace();
        var dispatcher = Build(new HeaderedHandler(HttpStatusCode.BadRequest, "Documento inválido"), trace: trace);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.Equal("Documento inválido", (string?)JsonNode.Parse(trace.Responses[TraceExchanges.Submit])!["response"]!["body"]);
        Assert.Contains("Documento inválido", ex.Reason);   // o motivo sai do mesmo corpo
    }

    [Fact]
    public async Task Status_response_with_body_overwrites_the_photo_and_204_does_not()
    {
        var handler = new SequencedHandler(
            (HttpStatusCode.OK, """{"id":"ext-1","status":"processando"}"""),
            (HttpStatusCode.NoContent, ""),
            (HttpStatusCode.OK, """{"id":"ext-1","status":"erro","mensagens":["CFOP inválido"]}"""),
            (HttpStatusCode.NotFound, ""));
        var trace = new RecordingTrace();
        var dispatcher = Build(handler, trace: trace);

        await dispatcher.CheckStatusAsync("ext-1", Context());
        string first = trace.Responses[TraceExchanges.Status];
        await dispatcher.CheckStatusAsync("ext-1", Context());
        Assert.Equal(first, trace.Responses[TraceExchanges.Status]);   // o 204 não sobrescreve
        await dispatcher.CheckStatusAsync("ext-1", Context());
        await dispatcher.CheckStatusAsync("ext-1", Context());         // nem o 404 sem corpo

        JsonNode photo = JsonNode.Parse(trace.Responses[TraceExchanges.Status])!;
        Assert.Equal("status", (string?)photo["exchange"]);
        Assert.Equal("GET", (string?)photo["request"]!["method"]);
        Assert.Equal("http://localhost/documents/ext-1/status", (string?)photo["request"]!["url"]);
        Assert.Equal("CFOP inválido", (string?)photo["response"]!["body"]!["mensagens"]![0]);
        Assert.Equal(2, trace.ResponseWrites);
        Assert.False(trace.Responses.ContainsKey(TraceExchanges.Submit));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Echoed_bearer_and_access_token_never_reach_the_photo_the_reason_the_log_or_the_exception(HttpStatusCode status)
    {
        const string token = "tok-segredo-123";
        var handler = new HeaderedHandler(status, $$"""{"mensagens":["recusado para Bearer {{token}}"],"access_token":"{{token}}"}""");
        var trace = new RecordingTrace();
        var logger = new CapturingLogger<AvalaraComplianceDispatcher>();
        var dispatcher = Build(handler, new FakeTokenProvider(token), trace, logger: logger);

        Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        string photo = trace.Responses[TraceExchanges.Submit];
        Assert.DoesNotContain(token, photo);
        Assert.True((int)JsonNode.Parse(photo)!["redactions"]! >= 2);
        Assert.DoesNotContain(token, ex.Message);
        Assert.DoesNotContain(token, ex.ToString());
        Assert.DoesNotContain(token, logger.All);
        if (ex is DispatchRejectedException rejected)
        {
            Assert.DoesNotContain(token, rejected.Reason);
            Assert.Contains("recusado para Bearer [redigido]", rejected.Reason);
        }
    }

    [Fact]
    public async Task Photo_failure_after_an_accept_does_not_change_the_outcome()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1","protocolo":"p-9"}""");
        var trace = new RecordingTrace { FailResponses = true };
        var logger = new CapturingLogger<AvalaraComplianceDispatcher>();
        var dispatcher = Build(handler, trace: trace, logger: logger);

        IntegrationReceipt receipt = await dispatcher.SubmitAsync(SampleInvoice(), Context());

        Assert.Equal("ext-guid-1", receipt.ExternalId);
        Assert.Equal(1, handler.RequestCount);
        (LogLevel level, string text) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("nfe-1", text);
        Assert.DoesNotContain("p-9", text);          // sem o conteúdo da resposta
        Assert.DoesNotContain("blob fora do ar", text);
    }

    [Fact]
    public async Task CheckStatus_propagates_on_non_success_status()
    {
        var handler = new StubHttpMessageHandler("""{"error":"boom"}""", HttpStatusCode.InternalServerError);
        var dispatcher = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.CheckStatusAsync("ext-guid-1", Context()));
    }

    // ---------- contrato novo: códigos da configuração, recusa do conector, omissões (ADR-0026) ----------

    [Fact]
    public async Task Company_codes_come_from_the_outbound_settings_and_not_from_the_erp()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        GoodsInvoice invoice = SampleInvoice() with
        {
            Issuance = Issuance.Own,
            Issuer = new Party { TaxId = "44278225000180", Name = "Contoso Entertainment System Brazil" },
        };
        var dispatcher = Build(handler, profiles: Profile(Section("44278225000180")));

        await dispatcher.SubmitAsync(invoice, Context());

        using var body = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("20247332000182", body.RootElement.GetProperty("codigoEmpresa").GetString());
        Assert.Equal("20247332000182", body.RootElement.GetProperty("codigoContribuinte").GetString());
        Assert.Equal("98765432000110", body.RootElement.GetProperty("parceiro").GetProperty("cnpj").GetString());
    }

    [Fact]
    public async Task Tenant_without_the_codes_is_rejected_before_any_request()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var dispatcher = Build(handler, profiles: Profile("""{"sandbox":{"baseUrl":"https://avalara-a/"}}"""));

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(SampleInvoice(), Context()));

        Assert.StartsWith("Configuração do conector:", ex.Reason);
        Assert.Contains("establishments", ex.Reason);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Data_the_contract_cannot_represent_is_rejected_before_any_request()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        GoodsInvoice invoice = SampleInvoice() with { Items = [SampleInvoice().Items[0] with { Cfop = "" }] };
        var dispatcher = Build(handler);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(invoice, Context()));

        Assert.StartsWith("Contrato do destino:", ex.Reason);
        Assert.Contains("CFOP", ex.Reason);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Receipt_carries_the_omissions()
    {
        var handler = new StubHttpMessageHandler("""{"id":"ext-guid-1"}""");
        var dispatcher = Build(handler);

        IntegrationReceipt receipt = await dispatcher.SubmitAsync(WithCharge(SampleInvoice()), Context());

        Assert.Equal(["item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo)"], receipt.Omissions);
    }

    // ---------- a omissão sai do motivo de falha e vai para a foto do envio (establishment-and-readable-dashboard, D9) ----------

    private const string ChargeOmission = "item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo)";

    [Fact]
    public async Task Platform_refusal_leaves_the_omissions_out_of_the_reason_and_in_the_photo()
    {
        var handler = new StubHttpMessageHandler("""{"mensagens":["codigoEmpresa não cadastrado"]}""", HttpStatusCode.BadRequest);
        var trace = new RecordingTrace();
        var dispatcher = Build(handler, trace: trace);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(WithCharge(SampleInvoice()), Context()));

        Assert.Equal("Plataforma de compliance recusou: codigoEmpresa não cadastrado", ex.Reason);
        Assert.DoesNotContain("Enviado sem", ex.Reason);
        Assert.Equal([ChargeOmission], Omissions(trace));
        Assert.NotNull(trace.Outbound);   // a foto do destino já estava salva quando a plataforma recusou
    }

    [Fact]
    public async Task Accepted_submission_photographs_the_omissions_and_keeps_them_in_the_receipt()
    {
        var trace = new RecordingTrace();
        var dispatcher = Build(new StubHttpMessageHandler("""{"id":"ext-guid-1"}"""), trace: trace);

        IntegrationReceipt receipt = await dispatcher.SubmitAsync(WithCharge(SampleInvoice()), Context());

        Assert.Equal([ChargeOmission], receipt.Omissions);
        Assert.Equal([ChargeOmission], Omissions(trace));
    }

    [Fact]
    public async Task Submission_without_omissions_has_no_omissions_field_in_the_photo()
    {
        var trace = new RecordingTrace();
        var dispatcher = Build(new StubHttpMessageHandler("""{"id":"ext-guid-1"}"""), trace: trace);

        await dispatcher.SubmitAsync(SampleInvoice(), Context());

        Assert.False(JsonNode.Parse(trace.Responses[TraceExchanges.Submit])!["request"]!.AsObject().ContainsKey("omissions"));
    }

    private static string[] Omissions(RecordingTrace trace)
        => [.. JsonNode.Parse(trace.Responses[TraceExchanges.Submit])!["request"]!["omissions"]!.AsArray().Select(o => (string)o!)];

    private static GoodsInvoice WithCharge(GoodsInvoice invoice)
        => invoice with { Items = [invoice.Items[0] with { Charges = [new ItemCharge { Number = 1, Kind = ChargeKind.Other, Amount = 416.25m }] }] };

    // Perfil padrão: o emitente da nota de exemplo (12345678000190) é o estabelecimento próprio, e a seção tem a URL do
    // mock em loopback e a credencial do tenant.
    private static FakeProfileStore Profile(string? outboundSettings = null)
        => new(new TenantConnectorProfile
        {
            TenantId = "tenant-a",
            Environment = "Sandbox",
            InboundAdapter = "Xml",
            OutboundAdapter = "Avalara",
            OutboundSettings = outboundSettings ?? Section("12345678000190"),
        });

    private static string Section(string ownCnpj) => $$"""
        {"sandbox":{"baseUrl":"http://localhost/","clientId":"id-a","clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret",
                    "establishments":{"{{ownCnpj}}":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"} } } }
        """;

    /// <summary>Responde com os cabeçalhos de uma plataforma real: os da lista e os que nunca entram na foto.</summary>
    private sealed class HeaderedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            response.Headers.Date = Now;
            response.Headers.Add("X-Correlation-Id", "corr-da-plataforma");
            response.Headers.Add("Set-Cookie", "sessao=abc; HttpOnly");
            response.Headers.Add("WWW-Authenticate", "Bearer realm=\"avalara\"");
            return Task.FromResult(response);
        }
    }

    /// <summary>Responde em sequência e guarda o cabeçalho de autorização de cada pedido.</summary>
    private sealed class SequencedHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            (HttpStatusCode status, string body) = responses[_next++];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 27, 15, 0, 0, TimeSpan.Zero);

    private static AvalaraComplianceDispatcher Build(
        HttpMessageHandler handler,
        IAvalaraTokenProvider? token = null,
        IProcessingTrace? trace = null,
        IConnectorProfileStore? profiles = null,
        ILogger<AvalaraComplianceDispatcher>? logger = null,
        string documentsPath = "documents")
    {
        var http = new HttpClient(handler);   // sem BaseAddress: toda URI é absoluta, da seção do tenant
        var options = Options.Create(new AvalaraOptions { Destination = "avalara", DocumentsPath = documentsPath });
        return new AvalaraComplianceDispatcher(
            http, options, token ?? new FakeTokenProvider("tok-padrao"), trace ?? new NoOpProcessingTrace(),
            profiles ?? Profile(), NoListing(), logger ?? new CapturingLogger<AvalaraComplianceDispatcher>(), new FixedClock(Now));
    }

    // Estes testes são o caminho da tabela: o resolvedor sem listagem é o destino que não lista, e a tabela é a única
    // fonte, como antes da change platform-establishment-resolution. A plataforma tem os testes dela.
    private static PlatformEstablishmentResolver NoListing() => new([], new PlatformEstablishmentOptions(), TimeProvider.System);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeProfileStore(TenantConnectorProfile? profile) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(profile);
        public Task UpsertAsync(TenantConnectorProfile p, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([]);
    }

    private sealed class FakeTokenProvider(Func<string, string> tokenOf, bool fresh = false) : IAvalaraTokenProvider
    {
        public FakeTokenProvider(string token, bool fresh = false) : this(_ => token, fresh)
        {
        }

        public List<AvalaraOutboundSettings> Asked { get; } = [];

        public List<AvalaraAccessToken> Invalidated { get; } = [];

        public Task<AvalaraAccessToken> GetTokenAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
        {
            Asked.Add(settings);
            return Task.FromResult(new AvalaraAccessToken(settings.TenantId, settings.Environment, tokenOf(settings.TenantId), fresh, key: null));
        }

        public void Invalidate(AvalaraAccessToken t) => Invalidated.Add(t);

        public void Forget(string tenantId)
        {
        }

        public Task<CredentialTestOutcome> ProbeAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingTrace : IProcessingTrace
    {
        public (string Tenant, string Key, string Json)? Domain { get; private set; }
        public (string Tenant, string Key, string Destination, string Json)? Outbound { get; private set; }

        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        {
            Domain = (tenantId, naturalKey, json);
            return Task.CompletedTask;
        }

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default)
        {
            Outbound = (tenantId, naturalKey, destination, json);
            return Task.CompletedTask;
        }

        /// <summary>A última resposta gravada de cada troca, como gravada (o envelope em JSON).</summary>
        public Dictionary<string, string> Responses { get; } = [];

        public int ResponseWrites { get; private set; }

        public bool FailResponses { get; set; }

        public Task SaveResponseAsync(string tenantId, string naturalKey, string destination, string exchange, string json, CancellationToken ct = default)
        {
            if (FailResponses)
            {
                throw new InvalidOperationException("blob fora do ar");
            }

            ResponseWrites++;
            Responses[exchange] = json;
            return Task.CompletedTask;
        }
    }

    private static DispatchContext Context(string tenant = "tenant-a") => new()
    {
        TenantId = tenant,
        NaturalKey = "nfe-1",
        CorrelationId = "corr-1",
        Operation = DocumentStatus.Issued,
    };

    internal static GoodsInvoice SampleInvoice() => new()
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
