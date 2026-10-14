namespace FiscalHub.Application.Connectors;

/// <summary>
/// O ambiente da plataforma de compliance contra o qual esta instância integra (ADR-0036 §3). Cada instância atende um
/// cliente e um ambiente, e o ambiente é dela, e não do perfil de conector.
/// <para>Não é o <c>IHostEnvironment</c>: aquele diz como o host se comporta (Development, Production), e este, contra que
/// ambiente da plataforma o cliente integra. Um host em Development aponta para o Sandbox, e os dois eixos não coincidem
/// (change <c>platform-account-and-connectors</c>, D6).</para>
/// </summary>
public interface IInstanceEnvironment
{
    /// <summary><c>Sandbox</c> ou <c>Production</c>, na grafia aceita.</summary>
    string Name { get; }
}

/// <summary>
/// O ambiente lido de <see cref="ConfigurationKey"/> na subida. Sem o valor, ou com um valor fora dos dois, o host não
/// sobe: um padrão silencioso aqui é a mesma classe de erro que a mudança veio apagar (D6).
/// </summary>
public sealed class InstanceEnvironment : IInstanceEnvironment
{
    public const string ConfigurationKey = "Connector:Environment";

    public const string Sandbox = "Sandbox";

    public const string Production = "Production";

    private static readonly string[] Accepted = [Sandbox, Production];

    private InstanceEnvironment(string name) => Name = name;

    public string Name { get; }

    /// <summary>O ambiente configurado, na grafia aceita. Lança, citando a chave, sem o valor ou com um valor que não é um dos dois.</summary>
    public static InstanceEnvironment From(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} não está configurado: diga contra que ambiente da plataforma esta instância integra "
                + $"({Sandbox} ou {Production}). O host não presume um.");
        }

        string? name = Accepted.FirstOrDefault(a => string.Equals(a, configured.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is not null
            ? new InstanceEnvironment(name)
            : throw new InvalidOperationException(
                $"{ConfigurationKey} = '{configured}' não é um ambiente da plataforma: os aceitos são {Sandbox} e {Production}.");
    }

    public override string ToString() => Name;
}
