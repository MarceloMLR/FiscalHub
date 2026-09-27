using Microsoft.Extensions.Configuration;

namespace FiscalHub.Infrastructure;

/// <summary>
/// O que o seed de dev semeia (seção <c>Seed</c>). Usuários, tenants e perfis de conector são semeados sempre, porque
/// sem eles não há login nem credencial. A demonstração — notas com fotos, execuções e agendamentos — só com
/// <see cref="DemoData"/>, e só por escolha explícita: o gatilho dela é a tabela estar vazia, e um ambiente que a semeasse
/// por padrão ganharia documentos, execuções e agendamentos falsos. Ligada, limpar a base e subir o host também traz tudo
/// de volta, como se a limpeza tivesse falhado.
/// </summary>
public sealed class DevSeedOptions
{
    /// <summary>Semeia a demonstração. Opt-in: sem a chave, vale <c>false</c>.</summary>
    public bool DemoData { get; init; }

    /// <summary>Lê a seção <c>Seed</c>. Um valor que não é booleano é erro de configuração, na subida.</summary>
    public static DevSeedOptions From(IConfiguration configuration)
    {
        string? raw = configuration["Seed:DemoData"];
        if (raw is null)
        {
            return new DevSeedOptions();
        }

        return bool.TryParse(raw, out bool demoData)
            ? new DevSeedOptions { DemoData = demoData }
            : throw new InvalidOperationException($"Seed:DemoData precisa ser true ou false (veio '{raw}').");
    }
}
