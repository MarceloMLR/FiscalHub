using FiscalHub.Application.Connectors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Host.Tests;

/// <summary>
/// A trava da chave do conector <b>é chamada</b> na subida (change <c>adapter-key-guard-proof</c>). A regra em si já
/// tinha teste — os <see cref="AdapterDeclarationGuardTests"/>, 4 casos. O que não tinha era <b>quem a chama</b>: uma
/// linha do <c>Program.cs</c>, logo depois do <c>builder.Build()</c>. Apagá-la não quebrava teste nenhum; a trava
/// simplesmente deixava de existir, em silêncio.
/// <para>Por isso este teste compõe o host pelo <b>caminho real da subida</b>, com
/// <c>WebApplicationFactory&lt;Program&gt;</c>, que invoca o entry point de verdade — e não uma cópia da composição.
/// Uma cópia provaria a cópia, que é exatamente o buraco. O teste é indiferente à forma da ligação: se amanhã a trava
/// passar a ser chamada por um <c>IStartupFilter</c> ou por outra extensão, ele continua valendo.</para>
/// </summary>
[Collection(HostStartupCollection.Name)]
public sealed class HostStartupGuardTests
{
    /// <summary>
    /// O caso verde: duas plataformas declaram o mesmo nome de adapter, e o host <b>não sobe</b>.
    /// <para>A asserção é sobre o <b>tipo e a mensagem</b>, e isso não é capricho de texto. Apagada a chamada da trava,
    /// o que acontece não é "outra exceção": é a falha da primeira conexão com o banco, lá na frente. Um
    /// <c>Assert.Throws&lt;Exception&gt;</c> frouxo ficaria <b>verde</b> ali — verde exatamente no cenário que este
    /// teste existe para pegar. A mensagem é o que distingue "a trava recusou" de "a subida morreu por outro motivo",
    /// e ela é contratual: nomeia o adapter repetido e as plataformas que o declaram, para quem sobe o host saber qual
    /// pacote tirar.</para>
    /// </summary>
    [Fact]
    public void The_host_refuses_to_start_when_two_platforms_declare_the_same_adapter()
    {
        using var host = new DeadPortHostFactory();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());

        Assert.Contains("Avalara", error.Message, StringComparison.Ordinal);
        Assert.Contains("Outra", error.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// O host inteiro, com <b>toda</b> a infraestrutura apontada para portas onde não há nada escutando.
///
/// <para><b>Por que portas mortas, e não o <c>appsettings</c> do host.</b> O caminho <b>verde</b> não toca
/// infraestrutura nenhuma: a trava recusa antes da primeira conexão (<c>MigrateProcessingSchemaAsync</c> vem depois
/// dela). Mas o caminho <b>vermelho</b> toca — e o vermelho é o que acontece na máquina de quem acabou de mexer no
/// <c>Program.cs</c>. Medido: com a chamada da trava removida e os containers de dev de pé, este teste <b>subia o host
/// inteiro e escrevia no banco de dev</b> (migrate + seed). Um teste cujo modo de falha mexe na base de alguém é pior
/// do que o buraco que ele fecha. Com as portas mortas, o vermelho morre na conexão recusada, sem escrever em lugar
/// nenhum — e o teste deixa de depender de a infra local estar de pé, ou de não estar.</para>
///
/// <para><b>O custo de cada caso:</b> o verde é instantâneo (a exceção vem antes de qualquer E/S); o vermelho custa
/// dezenas de segundos, que é o tempo de o TCP desistir da porta morta. O vermelho lento é o preço de não escrever no
/// banco de ninguém, e é pago só por quem acabou de quebrar a trava. <b>O tempo também é a prova</b> de que o verde não
/// tocou infraestrutura: um teste que tentasse SQL custaria dezenas de segundos, e não centenas de milissegundos.</para>
///
/// <para><b>Por que variável de ambiente, e não <c>ConfigureAppConfiguration</c>.</b> O <c>Program.cs</c> lê a
/// configuração de forma <b>ansiosa</b>, no corpo do arquivo, antes do <c>builder.Build()</c> — o cofre, as três
/// cadeias de conexão, o ambiente da instância. O <c>ConfigureAppConfiguration</c> é um callback aplicado só <b>na</b>
/// construção do host, isto é, depois dessas leituras: medido com uma sonda (um <c>SecretStore:VaultUri</c> inválido
/// posto por ele), o host nem reparou — a sonda não chegou. Variável de ambiente é lida pelo
/// <c>WebApplication.CreateBuilder</c>, antes de tudo, e não é truque de teste: é a mesma porta por onde a configuração
/// entra em produção. (O <c>UseSetting</c>, medido pela mesma sonda, <b>chega</b> a tempo — ele entra na configuração
/// do host, e não no callback. Fica anotado porque a diferença entre os dois não é óbvia, e porque ele dispensaria o
/// isolamento abaixo.)</para>
///
/// <para><b>O cuidado que a variável de ambiente cria:</b> ela é do processo inteiro, e o xUnit roda classes em
/// paralelo. Por isso esta classe põe e retira as variáveis, e a coleção dela fica fora da execução paralela. Um teste
/// que contamina a vizinhança é pior do que o buraco que ele fecha.</para>
///
/// <para><b>Se este teste quebrar porque falta uma chave:</b> é informação, não ruído — a subida passou a exigir uma
/// configuração que não está na lista. Acrescente a chave aqui, com o porquê. <b>Limite conhecido:</b> o content root
/// resolve para o projeto do Host, então o <c>appsettings.json</c> dele é lido. Chave obrigatória nova que nasça só lá
/// não aparece aqui: esta fatia prova a chamada da trava, e não a completude da configuração.</para>
/// </summary>
internal sealed class DeadPortHostFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Toda a infraestrutura que o host exige na subida, em portas mortas. A porta 9 é a de descarte: não há nada
    /// escutando nela. O porquê de cada chave fica ao lado dela — é o que impede que um teste quebrado por configuração
    /// nova seja lido como "teste chato".
    /// </summary>
    private static readonly (string Key, string Value)[] DeadPorts =
    [
        // new BlobServiceClient(...) — monta o cliente na subida, com a cadeia. Formato de Azurite, endpoint morto.
        ("ConnectionStrings__Blob",
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;"
            + "AccountKey=bmFvLWhhLW5pbmd1ZW0tbmVzdGEtcG9ydGE=;BlobEndpoint=http://127.0.0.1:9/devstoreaccount1;"),

        // AddSqlProcessingStore(...) — e é esta a conexão que o caminho vermelho tenta, e não consegue.
        ("ConnectionStrings__Sql", "Server=127.0.0.1,9;Database=FiscalHub-porta-morta;User Id=sa;"
            + "Password=nao-ha-ninguem-nesta-porta;TrustServerCertificate=true"),

        // AddServiceBusDocumentQueue(...) — a cadeia é guardada na subida; o cliente só nasce ao consumir.
        ("ConnectionStrings__ServiceBus",
            "Endpoint=sb://127.0.0.1:9;SharedAccessKeyName=nao-usado;SharedAccessKey=bmFvLXVzYWRv;"),

        // AddKeyVaultSecretStore(...) — "sem a seção, o host não sobe". A URI precisa ser https absoluta, e a
        // credencial 'Emulator' e a impressão fixada só valem em loopback: as três condições, numa porta morta.
        ("SecretStore__VaultUri", "https://127.0.0.1:9/"),
        ("SecretStore__Credential", "Emulator"),
        ("SecretStore__EmulatorCertificateThumbprint", "0000000000000000000000000000000000000000"),

        // FillPendingIntegrationPathAsync(...) — só o caminho vermelho chega lá, e nem ele passa do SQL.
        ("Avalara__DocumentsPath", "porta-morta/nao-usado"),
    ];

    private readonly (string Key, string? Value)[] _previous;

    public DeadPortHostFactory()
    {
        // Postas antes de qualquer coisa: o CreateBuilder do Program.cs as lê na primeira linha. Guardamos o valor de
        // antes — inclusive o "não existia" — para devolver o processo como estava.
        _previous = [.. DeadPorts.Select(p => (p.Key, Environment.GetEnvironmentVariable(p.Key)))];
        foreach ((string key, string value) in DeadPorts)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>
    /// A declaração em conflito com a <c>Avalara</c> que o host registra de verdade: mesma chave de adapter, outra
    /// plataforma. O <c>ConfigureServices</c> chega a tempo porque corre <b>durante</b> a construção do host, e a trava
    /// confere depois dela, sobre as declarações já reunidas no contêiner.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(services =>
            services.AddSingleton<IAdapterDeclaration>(new FakeAdapterDeclaration("Outra", "Avalara")));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach ((string key, string? value) in _previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// As variáveis de ambiente são do processo inteiro: esta coleção fica fora da execução paralela, para que o host
/// composto aqui não mude a configuração debaixo de nenhum outro teste.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostStartupCollection
{
    public const string Name = "host-startup";
}
