using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiscalHub.Adapters.Ingress.D365Poll;
using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Tracing;
using FiscalHub.Application.Validation;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalHub.Integration.Tests;

/// <summary>
/// Ponta a ponta com o contrato novo (ADR-0026, design D13 e D15): nota D365 GRAVADA → esteira com o validador real →
/// dispatcher REAL da Avalara, na composição padrão (autenticado, ADR-0027) → mock de compliance em memória, que exige o
/// token → poll de status. O segredo entra pelo <see cref="ConnectorProfileService"/>, como pela tela, e vai para um
/// cofre em memória. O que chega ao mock é o payload que a plataforma receberia. O store é falso e registra o que a
/// esteira e o poll lhe entregam; a preservação da observação na confirmação é do SqlProcessingStore.
/// </summary>
public class DispatchToMockTests
{
    private const string OutgoingKey = "brmf|BRMF21-10000026";
    private const string ImportKey = "brmf|BRMF06-110000031";

    // O caminho de envio do sandbox, o mesmo do appsettings.Development.json.
    private const string SandboxDocumentsPath = "taxcompliance/v2/fiscal/dfe";

    [Fact]
    public async Task Recorded_d365_note_is_sent_with_the_new_contract_and_confirmed()
    {
        using Harness h = await Harness.CreateAsync();
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.Submitted, row.Status);
        Assert.Empty(row.Receipt!.Omissions);

        using JsonDocument payload = await h.SentPayloadAsync(row.Receipt.ExternalId);
        JsonElement root = payload.RootElement;
        Assert.Equal("20247332000182", root.GetProperty("codigoEmpresa").GetString());      // da configuração, não do ERP
        Assert.Equal("20247332000182", root.GetProperty("codigoContribuinte").GetString());
        Assert.Equal("Southridge Video Brasil Ltda", root.GetProperty("parceiro").GetProperty("nome").GetString());
        Assert.Equal(OutgoingKey, root.GetProperty("codigoReferenciaIntegracao").GetString());
        Assert.All(root.GetProperty("itens").EnumerateArray(), item =>
        {
            JsonElement imposto = item.GetProperty("imposto");
            Assert.True(imposto.TryGetProperty("icms", out _));
            Assert.True(imposto.TryGetProperty("pis", out _));
            Assert.True(imposto.TryGetProperty("cofins", out _));
            Assert.False(item.TryGetProperty("impostos", out _));   // nota de 2016: sem grupo da Reforma, sem array
        });
        Assert.Equal(51, root.GetProperty("itens")[0].GetProperty("imposto").GetProperty("ipi").GetProperty("situacaoTributariaIPI").GetInt32());

        await h.PollAsync();

        Assert.Equal((IntegrationStatus.Confirmed, (string?)null), h.Store.Polled[OutgoingKey]);
    }

    [Fact]
    public async Task Import_note_is_sent_with_the_import_tax_in_its_block_and_the_charge_declared_as_omitted()
    {
        using Harness h = await Harness.CreateAsync();
        h.ServeNote("35637156586", withAccounting: true, "postaladdress-22565428565", "city-22565694955", "postaladdress-22565426303");

        await h.ProcessAsync(ImportKey, "35637156586");

        StoredRow row = h.Store.Rows[ImportKey];
        Assert.Equal(IntegrationStatus.Submitted, row.Status);
        Assert.Equal(["item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo)"], row.Receipt!.Omissions);

        using JsonDocument payload = await h.SentPayloadAsync(row.Receipt.ExternalId);
        JsonElement ii = payload.RootElement.GetProperty("itens")[0].GetProperty("imposto").GetProperty("ii");
        Assert.Equal(4500m, ii.GetProperty("baseCalculoII").GetDecimal());    // a TaxBase, completada pela contábil (D9)
        Assert.Equal(30m, ii.GetProperty("aliquotaII").GetDecimal());
        Assert.Equal(1350m, ii.GetProperty("valorII").GetDecimal());
        Assert.False(payload.RootElement.GetProperty("parceiro").TryGetProperty("cnpj", out _));   // fornecedor estrangeiro

        await h.PollAsync();

        // A confirmação volta sem motivo novo — o store mantém a observação do envio (SqlProcessingStoreTests).
        Assert.Equal((IntegrationStatus.Confirmed, (string?)null), h.Store.Polled[ImportKey]);
    }

    [Fact]
    public async Task Platform_error_on_status_is_recorded_with_the_platform_reason()
    {
        using Harness h = await Harness.CreateAsync();
        await h.SetMockResultAsync("erro", "CFOP 6101 incompatível com a operação");
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");
        await h.PollAsync();

        Assert.Equal(
            (IntegrationStatus.IntegrationError, "Plataforma de compliance rejeitou: CFOP 6101 incompatível com a operação"),
            h.Store.Polled[OutgoingKey]);
    }

    [Fact]
    public async Task Platform_refusal_on_submission_is_recorded_with_the_platform_summary_after_a_single_post()
    {
        // O mock recusa como o sandbox: ProblemDetails com o mapa errors (establishment-and-readable-dashboard, D15).
        var trace = new RecordingTrace();
        using Harness h = await Harness.CreateAsync(trace: trace);
        await h.SetMockResultAsync("rejeitar", "codigoEmpresa não cadastrado");
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);
        Assert.Equal("Plataforma de compliance recusou: 1 campo com erro: documento", row.Reason);   // o resumo, sem o title
        Assert.Equal(1, h.DocumentPosts);

        // A foto: a lista inteira, sem o ruído do ProblemDetails.
        using JsonDocument photo = JsonDocument.Parse(trace.Responses[(OutgoingKey, TraceExchanges.Submit)]);
        JsonElement body = photo.RootElement.GetProperty("response").GetProperty("body");
        Assert.Equal("codigoEmpresa não cadastrado", body.GetProperty("errors").GetProperty("documento")[0].GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        Assert.False(body.TryGetProperty("type", out _));
        Assert.False(body.TryGetProperty("title", out _));
        Assert.False(body.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Refused_note_with_an_omission_keeps_it_in_the_photo_and_out_of_the_reason()
    {
        var trace = new RecordingTrace();
        using Harness h = await Harness.CreateAsync(trace: trace);
        await h.SetMockResultAsync("rejeitar", "codigoEmpresa não cadastrado");
        h.ServeNote("35637156586", withAccounting: true, "postaladdress-22565428565", "city-22565694955", "postaladdress-22565426303");

        await h.ProcessAsync(ImportKey, "35637156586");

        StoredRow row = h.Store.Rows[ImportKey];
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);
        Assert.DoesNotContain("Enviado sem", row.Reason);
        using JsonDocument photo = JsonDocument.Parse(trace.Responses[(ImportKey, TraceExchanges.Submit)]);
        Assert.Equal(
            "item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo)",
            photo.RootElement.GetProperty("request").GetProperty("omissions")[0].GetString());
    }

    // ---------- autenticação contra o mock (ADR-0027) ----------

    [Fact]
    public async Task Every_request_to_the_mock_carries_the_token_it_issued()
    {
        using Harness h = await Harness.CreateAsync();
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");
        await h.PollAsync();

        Assert.Equal(IntegrationStatus.Submitted, h.Store.Rows[OutgoingKey].Status);
        Assert.Equal(IntegrationStatus.Confirmed, h.Store.Polled[OutgoingKey].Status);
        Assert.Equal(1, h.TokenRequests);                                   // um token para o envio e a consulta
        Assert.All(h.DocumentAuthorizations, a => Assert.StartsWith("Bearer ", a));
    }

    [Fact]
    public async Task Credential_refused_by_the_platform_is_a_rejection_with_the_reason_and_no_document_post()
    {
        using Harness h = await Harness.CreateAsync();
        await h.SetMockTokenAsync("recusar");
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);
        Assert.StartsWith("Configuração do conector:", row.Reason);
        Assert.Contains("(HTTP 400: client_id invalid)", row.Reason);                            // a forma do sandbox
        Assert.EndsWith("Confira o Client ID e o Client Secret na tela de conectores.", row.Reason);
        Assert.Contains("'tenant-a'", row.Reason);
        Assert.DoesNotContain(Harness.Secret, row.Reason);
        Assert.Equal(1, h.TokenRequests);
        Assert.Equal(0, h.DocumentPosts);
    }

    [Fact]
    public async Task Saving_the_profile_forgets_the_refusal_and_the_reprocessed_note_goes_out()
    {
        using Harness h = await Harness.CreateAsync();
        await h.SetMockTokenAsync("recusar");
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");
        await h.ProcessAsync(OutgoingKey, "35637156582");

        // A plataforma libera o cliente. Sem salvar o perfil, a recusa ainda é lembrada: nenhum pedido de token.
        await h.SetMockTokenAsync("aceitar");
        h.ServeNote("35637156582");
        await h.ProcessAsync(OutgoingKey, "35637156582");
        Assert.Equal(IntegrationStatus.IntegrationError, h.Store.Rows[OutgoingKey].Status);
        Assert.Equal(1, h.TokenRequests);

        // Salvar o perfil pela tela, sem mudar nada: o token é pedido na hora, e a nota sai.
        Assert.Equal(ConnectorProfileSaveStatus.Saved, (await h.SaveProfileAsync(clientSecret: null)).Status);
        h.ServeNote("35637156582");
        await h.ProcessAsync(OutgoingKey, "35637156582");

        Assert.Equal(IntegrationStatus.Submitted, h.Store.Rows[OutgoingKey].Status);
        Assert.Equal(2, h.TokenRequests);
        Assert.Equal(1, h.DocumentPosts);
    }

    [Fact]
    public async Task The_mock_answers_on_the_sandbox_submit_path_like_the_platform()
    {
        using Harness h = await Harness.CreateAsync(documentsPath: SandboxDocumentsPath);
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");
        await h.PollAsync();

        Assert.Equal(IntegrationStatus.Submitted, h.Store.Rows[OutgoingKey].Status);
        Assert.Equal(IntegrationStatus.Confirmed, h.Store.Polled[OutgoingKey].Status);
        Assert.All(h.DocumentPaths, p => Assert.StartsWith("/" + SandboxDocumentsPath, p));   // envio e consulta
        Assert.Equal(2, h.DocumentPaths.Count);
    }

    [Fact]
    public async Task Wrong_submit_path_is_a_configuration_rejection_after_a_single_post()
    {
        using Harness h = await Harness.CreateAsync(documentsPath: "taxcompliance/v1/caminho-errado");
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);                            // e não a dead-letter
        Assert.Contains("o caminho de envio não existe nessa URL", row.Reason);
        Assert.Contains("OutboundSettings.sandbox.baseUrl", row.Reason);
        Assert.Contains("Avalara:DocumentsPath", row.Reason);
        Assert.Contains("taxcompliance/v1/caminho-errado", row.Reason);
        Assert.Equal(1, h.DocumentPosts);
    }

    [Fact]
    public async Task Recorded_sandbox_refusal_is_an_integration_error_with_its_reason_end_to_end()
    {
        // A recusa real do sandbox (2026-09-27), gravada pela quarta foto, no lugar da resposta do mock ao envio. A esteira
        // registra IntegrationError com o motivo dela, depois de um único POST, sem exceção — ou seja, sem dead-letter.
        using Harness h = await Harness.CreateAsync(documentsPath: SandboxDocumentsPath, recordedSubmit: RecordedSubmit("recusa-no-envio.json"));
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");   // não lança

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);
        Assert.Equal("Plataforma de compliance recusou: 6 campos com erro: operacao, tipoPagamento, parceiro.Codigo e mais 3", row.Reason);
        Assert.DoesNotContain("One or more validation errors occurred.", row.Reason);
        Assert.Equal(1, h.DocumentPosts);
    }

    [Fact]
    public async Task Missing_secret_is_a_rejection_pointing_to_the_screen_with_no_request()
    {
        using Harness h = await Harness.CreateAsync(clientSecret: null);
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);
        Assert.Contains("OutboundSettings.sandbox.clientSecret", row.Reason);
        Assert.Contains("Configurações → Conectores → Avalara → Sandbox → Client Secret", row.Reason);
        Assert.Equal(0, h.TokenRequests);
        Assert.Equal(0, h.DocumentPosts);
    }

    [Fact]
    public async Task Submit_and_status_responses_are_photographed()
    {
        var trace = new RecordingTrace();
        using Harness h = await Harness.CreateAsync(trace: trace);
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");
        await h.PollAsync();

        using JsonDocument submit = JsonDocument.Parse(trace.Responses[(OutgoingKey, TraceExchanges.Submit)]);
        using JsonDocument status = JsonDocument.Parse(trace.Responses[(OutgoingKey, TraceExchanges.Status)]);
        Assert.Equal(200, submit.RootElement.GetProperty("response").GetProperty("status").GetInt32());
        Assert.Equal(h.Store.Rows[OutgoingKey].Receipt!.ExternalId, submit.RootElement.GetProperty("response").GetProperty("body").GetProperty("id").GetString());
        Assert.Equal("carregado", status.RootElement.GetProperty("response").GetProperty("body").GetProperty("status").GetString());
        Assert.All(trace.Responses.Values, photo => Assert.DoesNotContain("Bearer", photo));
    }

    // ---------- o CNPJ alfanumérico (tax-identifier-normalization) ----------

    [Fact]
    public async Task Alphanumeric_cnpj_crosses_discovery_assembly_and_dispatch_with_the_same_value()
    {
        // Derivada: a nota de saída gravada, com o CNPJ do estabelecimento e o do cliente trocados por alfanuméricos. A
        // tradução da Avalara tem a chave com a máscara, como alguém a digitaria.
        var trace = new RecordingTrace();
        using Harness h = await Harness.CreateAsync(
            trace: trace, establishments: """{"12.ABC.345/01DE-35":{"codigoEmpresa":"ALFA-EMP","codigoContribuinte":"ALFA-CTB"}}""");
        JsonObject header = JsonNode.Parse(Fixture(Path.Combine("d365", "notes", "35637156582", "header.json")))!["value"]![0]!.AsObject();
        header["FiscalEstablishmentCNPJCPF"] = "12.ABC.345/01DE-35";
        header["ThirdPartyCNPJCPF"] = "98.XYZ.765/0001-32";

        DocumentReference discovered = await h.DiscoverAsync(header);   // o feed real, sobre o mesmo cabeçalho
        h.ServeNote("35637156582", header, "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");
        await h.ProcessAsync(discovered);

        Assert.Equal("12ABC34501DE35", discovered.Metadata!.CompanyCode);           // o grupo da referência, e não 123450135
        using JsonDocument domain = JsonDocument.Parse(trace.Domain[OutgoingKey]);
        Assert.Equal("12ABC34501DE35", domain.RootElement.GetProperty("establishment").GetProperty("taxId").GetString());
        Assert.Equal("12ABC34501DE35", h.Store.Metadata[OutgoingKey].CompanyCode);  // o registro e o card

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.Submitted, row.Status);
        using JsonDocument payload = await h.SentPayloadAsync(row.Receipt!.ExternalId);
        Assert.Equal("ALFA-EMP", payload.RootElement.GetProperty("codigoEmpresa").GetString());           // a tradução achou a chave
        Assert.Equal("ALFA-CTB", payload.RootElement.GetProperty("codigoContribuinte").GetString());
        Assert.Equal("98XYZ765000132", payload.RootElement.GetProperty("parceiro").GetProperty("cnpj").GetString());
    }

    // ---------- o de/para do estabelecimento pela plataforma (platform-establishment-resolution) ----------

    // A conta do mock: a empresa 012 com os quatro estabelecimentos da brmf e o CNPJ dos XMLs, e as empresas de teste.
    // Os códigos não seguem a ordem do CNPJ.
    private static readonly (string Cnpj, string Codigo)[] Brmf =
    [
        ("442782250001-80", "010"),   // Matriz
        ("442782250002-60", "007"),   // SP-01
        ("442782250003-41", "021"),   // SAL-01
        ("442782250034-48", "003"),   // RJ-01
    ];

    // Uma listagem inteira da conta do mock com o $top 2: as empresas (3) em 2 páginas e a vazia; os contribuintes da 012 (5)
    // em 3 páginas e a vazia, e os da Comércio e da 009 (1 cada) em 1 e a vazia.
    private static readonly (int Companies, int Taxpayers) OneListingWithTopTwo = (3, 4 + 2 + 2);

    [Fact]
    public async Task The_mock_lists_companies_like_the_sandbox_in_an_envelope_with_the_query_and_in_an_array_without()
    {
        // As chamadas diretas ao sandbox (change platform-listing-shape): com as opções de query, o envelope, inclusive na
        // página vazia; sem nenhuma opção, o array puro.
        using Harness h = await Harness.CreateAsync();

        JsonNode withQuery = await h.MockListingPageAsync("taxcompliance/v2/empresa?$top=5&$orderby=empresaId");
        JsonNode emptyPage = await h.MockListingPageAsync("taxcompliance/v2/empresa?$top=5&$orderby=empresaId&$skip=999");
        JsonNode withoutQuery = await h.MockListingPageAsync("taxcompliance/v2/empresa");

        Assert.Equal(3, Assert.IsType<JsonObject>(withQuery)["value"]!.AsArray().Count);
        Assert.Equal("""{"value":[]}""", emptyPage.ToJsonString());
        Assert.Equal(3, Assert.IsType<JsonArray>(withoutQuery).Count);
    }

    [Fact]
    public async Task The_four_brmf_establishments_resolve_and_dispatch_with_an_empty_table()
    {
        using Harness h = await Harness.CreateAsync(establishments: "{}", listingPageSize: 2);

        await ProcessBrmfAsync(h);

        foreach ((string cnpj, string codigo) in Brmf)
        {
            StoredRow row = h.Store.Rows[Key(cnpj)];
            Assert.Equal(IntegrationStatus.Submitted, row.Status);
            using JsonDocument payload = await h.SentPayloadAsync(row.Receipt!.ExternalId);
            Assert.Equal("012", payload.RootElement.GetProperty("codigoEmpresa").GetString());
            Assert.Equal(codigo, payload.RootElement.GetProperty("codigoContribuinte").GetString());   // só com todas as páginas
        }

        Assert.Equal(OneListingWithTopTwo, await h.ListingRequestsAsync());   // o lote inteiro: uma listagem
    }

    [Fact]
    public async Task A_server_page_limit_below_the_top_still_resolves_the_four_establishments()
    {
        using Harness h = await Harness.CreateAsync(establishments: "{}", listingPageSize: 100);
        await h.MockListingAsync("listagem/limite?itens=2");   // o servidor devolve no máximo 2, com o $top 100

        await ProcessBrmfAsync(h);

        Assert.All(Brmf, b => Assert.Equal(IntegrationStatus.Submitted, h.Store.Rows[Key(b.Cnpj)].Status));
        Assert.Equal(4, h.DocumentPosts);
    }

    [Fact]
    public async Task A_batch_of_notes_lists_the_platform_once()
    {
        using Harness h = await Harness.CreateAsync(establishments: "{}", listingPageSize: 2);
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");
        await h.ProcessAsync(OutgoingKey, "35637156582");
        for (int i = 0; i < 9; i++)
        {
            h.ServeNote("35637156582");
            await h.ProcessAsync(OutgoingKey, "35637156582");
        }

        Assert.Equal(10, h.DocumentPosts);
        Assert.Equal(OneListingWithTopTwo, await h.ListingRequestsAsync());
    }

    [Fact]
    public async Task A_duplicate_on_the_platform_is_refused_naming_both_and_nothing_is_posted()
    {
        using Harness h = await Harness.CreateAsync(establishments: "{}");
        await h.MockListingAsync("contribuintes/adicionar?cnpj=44278225000180&empresa=009");

        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");
        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);
        Assert.Contains("o estabelecimento 44278225000180 tem 2 contribuintes na plataforma", row.Reason);
        Assert.Contains("empresa '012' (METALURGICA EXEMPLO (mock)), contribuinte '010' (#10001)", row.Reason);
        Assert.Contains("empresa '009' (LABORATORIO (mock)), contribuinte '001' (#90001)", row.Reason);
        Assert.Equal(0, h.DocumentPosts);
    }

    [Fact]
    public async Task A_cnpj_without_taxpayer_is_refused_naming_it_and_goes_out_after_restore_save_and_reprocess()
    {
        using Harness h = await Harness.CreateAsync(establishments: "{}");
        await h.MockListingAsync("contribuintes/remover?cnpj=44278225000180");

        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");
        await h.ProcessAsync(OutgoingKey, "35637156582");
        StoredRow refused = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.IntegrationError, refused.Status);
        Assert.Contains("o estabelecimento 44278225000180 não tem contribuinte cadastrado na plataforma", refused.Reason);

        // Cadastrado de novo na plataforma: sem salvar, a listagem guardada ainda vale e a nota continua recusada.
        await h.MockListingAsync("contribuintes/restaurar");
        h.ServeNote("35637156582");
        await h.ProcessAsync(OutgoingKey, "35637156582");
        Assert.Equal(IntegrationStatus.IntegrationError, h.Store.Rows[OutgoingKey].Status);

        // Salvar o perfil sem mudar nada faz o hub reler a plataforma, e o reprocesso sai.
        Assert.Equal(ConnectorProfileSaveStatus.Saved, (await h.SaveProfileAsync(clientSecret: null)).Status);
        h.ServeNote("35637156582");
        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow sent = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.Submitted, sent.Status);
        using JsonDocument payload = await h.SentPayloadAsync(sent.Receipt!.ExternalId);
        Assert.Equal("010", payload.RootElement.GetProperty("codigoContribuinte").GetString());
        Assert.Equal(1, h.DocumentPosts);
    }

    [Fact]
    public async Task An_alphanumeric_cnpj_on_the_platform_resolves_with_the_same_value()
    {
        using Harness h = await Harness.CreateAsync(establishments: "{}");
        await h.MockListingAsync("contribuintes/adicionar?cnpj=12ABC34501DE35&empresa=012&codigo=ALFA");
        JsonObject header = Header();
        header["FiscalEstablishmentCNPJCPF"] = "12.ABC.345/01DE-35";

        DocumentReference discovered = await h.DiscoverAsync(header);
        h.ServeNote("35637156582", header, "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");
        await h.ProcessAsync(discovered);

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.Submitted, row.Status);
        using JsonDocument payload = await h.SentPayloadAsync(row.Receipt!.ExternalId);
        Assert.Equal("012", payload.RootElement.GetProperty("codigoEmpresa").GetString());
        Assert.Equal("ALFA", payload.RootElement.GetProperty("codigoContribuinte").GetString());
    }

    [Fact]
    public async Task The_table_wins_over_the_platform_and_lists_nothing()
    {
        // A tabela de sempre do harness traduz a Matriz para 20247332000182; a plataforma tem a Matriz como 012/010.
        using Harness h = await Harness.CreateAsync();
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");

        using JsonDocument payload = await h.SentPayloadAsync(h.Store.Rows[OutgoingKey].Receipt!.ExternalId);
        Assert.Equal("20247332000182", payload.RootElement.GetProperty("codigoContribuinte").GetString());
        Assert.Equal((0, 0), await h.ListingRequestsAsync());
    }

    // As quatro notas da brmf: a nota de saída gravada da Matriz, com o estabelecimento e o voucher trocados por nota.
    private static async Task ProcessBrmfAsync(Harness h)
    {
        bool first = true;
        foreach ((string cnpj, _) in Brmf)
        {
            JsonObject header = Header();
            header["FiscalEstablishmentCNPJCPF"] = cnpj;
            header["Voucher"] = Voucher(cnpj);
            DocumentReference discovered = await h.DiscoverAsync(header);
            Assert.Equal(Key(cnpj), discovered.NaturalKey);
            if (first)
            {
                h.ServeNote("35637156582", header, "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");
                first = false;
            }
            else
            {
                h.ServeNote("35637156582", header);   // os cadastros de referência já estão no cache da montagem
            }

            await h.ProcessAsync(discovered);
        }
    }

    // A ordem do estabelecimento no CNPJ (0001, 0002, 0003, 0034) faz o voucher de cada nota.
    private static string Voucher(string cnpj) => $"BRMF21-{cnpj[8..12]}";

    private static string Key(string cnpj) => $"brmf|{Voucher(cnpj)}";

    private static JsonObject Header()
        => JsonNode.Parse(Fixture(Path.Combine("d365", "notes", "35637156582", "header.json")))!["value"]![0]!.AsObject();

    // ---------- apoio ----------

    private static string Fixture(string relative) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", relative));

    // O status e o corpo de um envelope gravado do sandbox (os mesmos arquivos dos testes do adapter, por link).
    private static (int Status, string Body) RecordedSubmit(string file)
    {
        using JsonDocument envelope = JsonDocument.Parse(Fixture(Path.Combine("sandbox", file)));
        JsonElement response = envelope.RootElement.GetProperty("response");
        return (response.GetProperty("status").GetInt32(), response.GetProperty("body").GetRawText());
    }

    private sealed class Harness : IDisposable
    {
        public const string Secret = "segredo-de-teste";

        // A Contoso traduzida para a empresa do JSON real.
        private const string ContosoEstablishments = """{"44278225000180":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"}}""";

        private readonly WebApplicationFactory<Program> _mock = new();
        private readonly CountingHandler _toMock;
        private ServiceProvider _services = null!;
        private InMemoryProfiles _profiles = null!;
        private ConnectorProfileService _profileService = null!;
        private string _establishments = ContosoEstablishments;
        private DocumentPipeline<GoodsInvoice> _pipeline = null!;
        private StatusPoller<GoodsInvoice> _poller = null!;

        private Harness() => _toMock = new CountingHandler(_mock.Server.CreateHandler());

        public SequencedHttp Http { get; } = new();

        public RecordingStore Store { get; } = new();

        public int DocumentPosts => _toMock.DocumentPosts;

        public int TokenRequests => _toMock.TokenRequests;

        public IReadOnlyList<string> DocumentAuthorizations => _toMock.DocumentAuthorizations;

        public IReadOnlyList<string> DocumentPaths => _toMock.DocumentPaths;

        /// <summary>O host em memória, com o perfil do tenant-a gravado pelo caso de uso da tela (o segredo vai ao cofre).</summary>
        public static async Task<Harness> CreateAsync(
            string? clientSecret = Secret, IProcessingTrace? trace = null, string documentsPath = "documents", (int Status, string Body)? recordedSubmit = null,
            string? establishments = null, int listingPageSize = 100)
        {
            var h = new Harness();
            h._toMock.RecordedSubmit = recordedSubmit;
            h._establishments = establishments ?? ContosoEstablishments;
            var profiles = new InMemoryProfiles();
            h._profiles = profiles;
            trace ??= new NoTrace();

            var services = new ServiceCollection();
            services.AddSingleton<IConnectorProfileStore>(profiles);
            services.AddSingleton<ISecretStore, InMemorySecrets>();
            services.AddSingleton(trace);
            services.AddAvalaraComplianceDispatcher(o =>   // a composição padrão: autenticada
            {
                o.DocumentsPath = documentsPath;
                o.ListingPageSize = listingPageSize;
            });
            // O resolvedor do de/para do estabelecimento, como o Host o registra: singleton, e observador do salvar do perfil.
            services.AddSingleton(new PlatformEstablishmentOptions());
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<PlatformEstablishmentResolver>();
            services.AddSingleton<IConnectorProfileObserver>(sp => sp.GetRequiredService<PlatformEstablishmentResolver>());
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => h._toMock));
            h._services = services.BuildServiceProvider();

            h._profileService = new ConnectorProfileService(
                profiles, h._services.GetRequiredService<ISecretStore>(), h._services.GetServices<IConnectorProfileObserver>(), new Tenant("tenant-a"));
            ConnectorProfileSaveResult saved = await h.SaveProfileAsync(clientSecret);
            Assert.Equal(ConnectorProfileSaveStatus.Saved, saved.Status);

            var dispatcher = h._services.GetRequiredService<IComplianceDispatcher<GoodsInvoice>>();
            var d365 = new D365GoodsInvoiceSource(
                new HttpClient(h.Http), profiles, new FakeTokens(), new D365ChangeFeedOptions(),
                new D365ReferenceDataCache(new D365AssemblyOptions(), TimeProvider.System), trace, TimeProvider.System,
                NullLogger<D365GoodsInvoiceSource>.Instance);
            h._pipeline = new DocumentPipeline<GoodsInvoice>(
                new InboundSourceResolver<GoodsInvoice>([d365], profiles), new GoodsInvoiceValidator(), dispatcher, h.Store, trace,
                new GoodsInvoiceMetadataExtractor());
            h._poller = new StatusPoller<GoodsInvoice>(h.Store, dispatcher, new StatusPollerOptions());
            return h;
        }

        /// <summary>Grava o perfil como a tela: o Client Secret, quando vem, é campo de escrita e vai para o cofre.</summary>
        public Task<ConnectorProfileSaveResult> SaveProfileAsync(string? clientSecret)
        {
            string secret = clientSecret is null ? string.Empty : $",\"clientSecret\":\"{clientSecret}\"";
            return _profileService.SaveAsync(new ConnectorProfileRequest(
                "Sandbox",
                "Dynamics365",
                """{"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"]}""",
                "Avalara",
                // O mock em memória em loopback, com a tradução de estabelecimentos do teste.
                $$$"""{"sandbox":{"baseUrl":"http://localhost/","clientId":"mock-client"{{{secret}}},"establishments":{{{_establishments}}}}}"""));
        }

        public void ServeNote(string recId, params string[] reference) => ServeNote(recId, withAccounting: false, reference);

        /// <summary>A nota gravada com um cabeçalho derivado no lugar do gravado; o resto, como gravado.</summary>
        public void ServeNote(string recId, JsonObject header, params string[] reference)
        {
            Http.Respond(new JsonObject { ["value"] = new JsonArray(header.DeepClone()) }.ToJsonString());
            foreach (string file in new[] { "lines", "taxes", "charges" })
            {
                Http.Respond(Fixture(Path.Combine("d365", "notes", recId, $"{file}.json")));
            }

            foreach (string file in reference)
            {
                Http.Respond(Fixture(Path.Combine("d365", "reference", $"{file}.json")));
            }
        }

        /// <summary>
        /// A referência que o feed real do D365 monta para o cabeçalho, com a origem que o poller põe ao publicar. O feed lê
        /// o $select dele do mesmo cabeçalho, mais o carimbo de alteração, que a fixture da montagem não traz.
        /// </summary>
        public async Task<DocumentReference> DiscoverAsync(JsonObject header)
        {
            JsonObject row = header.DeepClone().AsObject();
            row["SysModifiedDateTime"] = "2026-08-08T01:31:00Z";
            var http = new SequencedHttp();
            http.Respond(new JsonObject { ["value"] = new JsonArray(row) }.ToJsonString());
            var feed = new D365ChangeFeed(
                new HttpClient(http), _profiles, new FakeTokens(), new D365ChangeFeedOptions(), TimeProvider.System,
                NullLogger<D365ChangeFeed>.Instance);

            var items = new List<ChangeFeedItem>();
            await foreach (ChangeFeedPage page in feed.PullAsync("tenant-a", DateTimeOffset.UnixEpoch))
            {
                items.AddRange(page.Items);
            }

            return Assert.Single(items).Reference with { Origin = feed.Origin };
        }

        public void ServeNote(string recId, bool withAccounting, params string[] reference)
        {
            foreach (string file in new[] { "header", "lines", "taxes", "charges" })
            {
                Http.Respond(Fixture(Path.Combine("d365", "notes", recId, $"{file}.json")));
            }

            if (withAccounting)
            {
                Http.Respond(Fixture(Path.Combine("d365", "notes", recId, "taxtrans.json")));
            }

            foreach (string file in reference)
            {
                Http.Respond(Fixture(Path.Combine("d365", "reference", $"{file}.json")));
            }
        }

        public Task ProcessAsync(string naturalKey, string recId)
            => ProcessAsync(new DocumentReference
            {
                TenantId = "tenant-a",
                Type = DocumentType.GoodsInvoice55,
                NaturalKey = naturalKey,
                Locator = $"d365/brmf/{recId}",
                Origin = "Dynamics365",
            });

        public Task ProcessAsync(DocumentReference reference)
            => _pipeline.ProcessAsync(
                reference,
                new DispatchContext { TenantId = "tenant-a", NaturalKey = reference.NaturalKey, CorrelationId = "corr-1", Operation = DocumentStatus.Issued });

        public Task<int> PollAsync() => _poller.PollOnceAsync();

        /// <summary>O JSON exato que o mock recebeu (a inspeção do próprio mock, aberta).</summary>
        public async Task<JsonDocument> SentPayloadAsync(string externalId)
            => JsonDocument.Parse(await _mock.CreateClient().GetStringAsync($"documents/{externalId}"));

        public async Task SetMockResultAsync(string result, string reason)
        {
            using HttpResponseMessage response = await _mock.CreateClient().PostAsync($"admin/result/{result}?motivo={Uri.EscapeDataString(reason)}", null);
            response.EnsureSuccessStatusCode();
        }

        public async Task SetMockTokenAsync(string value)
        {
            using HttpResponseMessage response = await _mock.CreateClient().PostAsync($"admin/token/{value}", null);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>Um modo da listagem do mock (/admin/contribuintes/* e /admin/listagem/*), aberto como os outros toggles.</summary>
        public async Task MockListingAsync(string pathAndQuery)
        {
            using HttpResponseMessage response = await _mock.CreateClient().PostAsync($"admin/{pathAndQuery}", null);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>Uma página crua da listagem do mock, com um token emitido por ele, que a listagem exige como o envio.</summary>
        public async Task<JsonNode> MockListingPageAsync(string pathAndQuery)
        {
            using HttpClient client = _mock.CreateClient();
            using var tokenRequest = new StringContent(
                """{"grant_type":"client_credentials","client_id":"mock-client","client_secret":"segredo-de-teste"}""", Encoding.UTF8, "application/json");
            using HttpResponseMessage token = await client.PostAsync("oauth/token", tokenRequest);
            token.EnsureSuccessStatusCode();

            using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", (string)JsonNode.Parse(await token.Content.ReadAsStringAsync())!["access_token"]!);
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        }

        /// <summary>As requisições de listagem que o mock recebeu, por endpoint, cada página contando uma.</summary>
        public async Task<(int Companies, int Taxpayers)> ListingRequestsAsync()
        {
            JsonNode snapshot = JsonNode.Parse(await _mock.CreateClient().GetStringAsync("admin/contribuintes"))!;
            return ((int)snapshot["requisicoes"]!["empresas"]!, (int)snapshot["requisicoes"]!["contribuintes"]!);
        }

        public void Dispose()
        {
            _services.Dispose();
            _mock.Dispose();
        }
    }

    private sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly List<string> _authorizations = [];
        private readonly List<string> _paths = [];

        public int DocumentPosts { get; private set; }

        public int TokenRequests { get; private set; }

        public IReadOnlyList<string> DocumentAuthorizations => _authorizations;

        public IReadOnlyList<string> DocumentPaths => _paths;

        /// <summary>Uma resposta real gravada, devolvida no lugar da do mock a cada envio (o token continua o do mock).</summary>
        public (int Status, string Body)? RecordedSubmit { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/oauth/token")
            {
                TokenRequests++;
            }
            else if (!path.StartsWith("/admin", StringComparison.Ordinal))
            {
                // Envio e consulta, em qualquer caminho de envio: o /documents ou o do sandbox.
                _paths.Add(path);
                _authorizations.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
                if (request.Method == HttpMethod.Post)
                {
                    DocumentPosts++;
                    if (RecordedSubmit is { } recorded)
                    {
                        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)recorded.Status)
                        {
                            Content = new StringContent(recorded.Body, Encoding.UTF8, "application/problem+json"),
                        });
                    }
                }
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class InMemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string name, CancellationToken ct = default) => Task.FromResult(_values.GetValueOrDefault(name));

        public Task SetAsync(string name, string value, CancellationToken ct = default)
        {
            _values[name] = value;
            return Task.CompletedTask;
        }

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_values.ContainsKey(name) ? new SecretDescription(DateTimeOffset.UnixEpoch) : null);
    }

    private sealed class SequencedHttp : HttpMessageHandler
    {
        private readonly Queue<string> _bodies = new();

        public void Respond(string json) => _bodies.Enqueue(json);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = _bodies.Count > 0 ? _bodies.Dequeue() : throw new InvalidOperationException($"Requisição inesperada: {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeTokens : ID365TokenProvider
    {
        public Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default) => Task.FromResult("tok");
    }

    private sealed class InMemoryProfiles : IConnectorProfileStore
    {
        private readonly Dictionary<string, TenantConnectorProfile> _profiles = [];

        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_profiles.GetValueOrDefault(tenantId));

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default)
        {
            _profiles[profile.TenantId] = profile;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([]);
    }

    internal sealed record StoredRow(IntegrationStatus Status, IntegrationReceipt? Receipt, string? Reason);

    /// <summary>Registra o que a esteira e o poll entregam ao store; lista como pendente o que foi enviado.</summary>
    internal sealed class RecordingStore : IProcessingStore
    {
        public Dictionary<string, StoredRow> Rows { get; } = [];

        public Dictionary<string, (IntegrationStatus Status, string? Reason)> Polled { get; } = [];

        public Dictionary<string, DocumentMetadata> Metadata { get; } = [];

        public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, string contentHash, CancellationToken ct = default)
        {
            Metadata[reference.NaturalKey] = metadata;
            return Task.CompletedTask;
        }

        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
        {
            Rows[reference.NaturalKey] = new StoredRow(receipt.Status, receipt, null);
            return Task.CompletedTask;
        }

        public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        {
            Rows[reference.NaturalKey] = new StoredRow(IntegrationStatus.IntegrationError, null, reason);
            return Task.CompletedTask;
        }

        public Task RecordIgnoredAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PendingIntegration>>(
            [
                .. Rows.Where(r => r.Value.Status == IntegrationStatus.Submitted && !Polled.ContainsKey(r.Key))
                       .Select(r => new PendingIntegration { TenantId = "tenant-a", NaturalKey = r.Key, ExternalId = r.Value.Receipt!.ExternalId, Attempts = 0 }),
            ]);

        public Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default)
        {
            Polled[naturalKey] = (status, reason);
            return Task.CompletedTask;
        }
    }

    private sealed class NoTrace : IProcessingTrace
    {
        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveResponseAsync(string tenantId, string naturalKey, string destination, string exchange, string json, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Guarda a foto do domínio de cada documento e a última foto de resposta de cada (documento, troca).</summary>
    private sealed class RecordingTrace : IProcessingTrace
    {
        public Dictionary<(string Key, string Exchange), string> Responses { get; } = [];

        public Dictionary<string, string> Domain { get; } = [];

        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        {
            Domain[naturalKey] = json;
            return Task.CompletedTask;
        }

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveResponseAsync(string tenantId, string naturalKey, string destination, string exchange, string json, CancellationToken ct = default)
        {
            Responses[(naturalKey, exchange)] = json;
            return Task.CompletedTask;
        }
    }
}
