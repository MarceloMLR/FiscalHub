using FiscalHub.Application.Outbound;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// A suíte de conformidade de um adapter de saída. O contrato de <see cref="IComplianceDispatcher{TDocument}"/> diz que
/// <c>PrepareAsync</c> não envia, e nada no compilador garante isso: postar de dentro da preparação é um
/// <see cref="HttpClient"/> chamado no método errado, e tipo nenhum distingue "montar" de "postar" (ADR-0040 — o que o
/// desenho NÃO garante). Esta suíte é a mitigação, e não a garantia: quem escreve um adapter de saída herda-a.
/// <para>Ela nasce aqui, no projeto de teste da Avalara, porque não existe projeto de teste compartilhado entre adapters
/// e a Avalara é o único adapter de saída que existe. No dia em que houver um segundo, promovê-la a biblioteca comum —
/// e herdá-la — é a primeira tarefa; está nos Próximos passos do <c>docs/STATUS.md</c> porque nada no build obriga.</para>
/// </summary>
public abstract class ComplianceDispatcherContractTests<TDocument>
{
    /// <summary>O adapter em prova, falando com a plataforma pelo handler dado.</summary>
    protected abstract IComplianceDispatcher<TDocument> Dispatcher(HttpMessageHandler platform);

    /// <summary>Um documento que o adapter prepara até o fim, sem recusa de configuração nem de contrato, e o contexto dele.</summary>
    protected abstract (TDocument Document, DispatchContext Context) Preparable { get; }

    /// <summary>A resposta de um envio aceito, com o identificador que este adapter lê.</summary>
    protected abstract string AcceptedResponse { get; }

    [Fact]
    public async Task Preparing_makes_no_request_to_the_platform()
    {
        // A plataforma responde como se tivesse aceitado, de propósito: um adapter que poste aqui segue adiante sem
        // erro nenhum, e é pego pela contagem. Se a prova dependesse de a requisição estourar, bastaria engolir a
        // exceção para "passar".
        var platform = new StubHttpMessageHandler(AcceptedResponse);
        (TDocument document, DispatchContext context) = Preparable;

        await Dispatcher(platform).PrepareAsync(document, context);

        Assert.True(
            platform.RequestCount == 0,
            $"PrepareAsync fez {platform.RequestCount} requisição(ões) à plataforma; a última foi "
            + $"{platform.LastRequest?.Method} {platform.LastRequest?.RequestUri}. Preparar traduz e fotografa o payload; "
            + "quem envia é SendAsync, e só ele (ADR-0040).");
    }

    [Fact]
    public async Task Sending_what_was_prepared_does_make_the_request()
    {
        // O par do teste de cima. Sem ele, um adapter que não preparasse nada e nunca enviasse também "passaria" na
        // conformidade — a suíte estaria provando silêncio, e não a ordem.
        var platform = new StubHttpMessageHandler(AcceptedResponse);
        (TDocument document, DispatchContext context) = Preparable;
        IComplianceDispatcher<TDocument> dispatcher = Dispatcher(platform);

        PreparedDispatch<TDocument> prepared = await dispatcher.PrepareAsync(document, context);
        await dispatcher.SendAsync(prepared, context);

        Assert.Equal(1, platform.RequestCount);
    }
}
