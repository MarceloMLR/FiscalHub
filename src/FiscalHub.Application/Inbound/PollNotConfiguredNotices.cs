namespace FiscalHub.Application.Inbound;

/// <summary>
/// Quando repetir o aviso de "poll desligado por ausência de configuração" (<see cref="ChangeFeedPassSummary.PollNotConfigured"/>).
/// Um coletor desligado em silêncio é o pior modo de falha, e o aviso não pode sumir do log do dia; mas repetido a cada
/// tick ele afoga o resto. A regra: avisa na primeira passada, repete a cada <c>interval</c> enquanto o problema continuar,
/// e avisa de novo na hora se ele voltar depois de corrigido. O estado é do processo (a casca do worker o guarda).
/// </summary>
public sealed class PollNotConfiguredNotices(TimeSpan interval)
{
    /// <summary>O texto do aviso, como modelo de log estruturado (<c>{Origin}</c> e <c>{Tenant}</c>).</summary>
    public const string Message =
        "Feed de mudanças: o perfil {Origin} do tenant {Tenant} não tem a seção poll nas InboundSettings, e o poll está "
        + "desligado por ausência de configuração: nenhuma nota nova será descoberta. Isso costuma vir de um perfil gravado "
        + "pela tela antiga de conectores, que apagava a seção (a tela de hoje a preserva, mas não a cria). Para ligar, "
        + "grave poll.enabled = true (docs/RUNNING.md §6); para deixar desligado de propósito, grave poll.enabled = false.";

    private readonly Dictionary<string, DateTimeOffset> _lastWarned = new(StringComparer.Ordinal);

    /// <summary>Dos tenants sem a seção nesta passada, os que devem ser avisados agora.</summary>
    public IReadOnlyList<string> Due(IEnumerable<string> tenants, DateTimeOffset now)
    {
        HashSet<string> current = [.. tenants];

        // Corrigido: esquece, para avisar na hora se voltar.
        foreach (string fixedTenant in _lastWarned.Keys.Where(t => !current.Contains(t)).ToList())
        {
            _lastWarned.Remove(fixedTenant);
        }

        var due = new List<string>();
        foreach (string tenant in current.Order(StringComparer.Ordinal))
        {
            if (!_lastWarned.TryGetValue(tenant, out DateTimeOffset last) || now - last >= interval)
            {
                _lastWarned[tenant] = now;
                due.Add(tenant);
            }
        }

        return due;
    }
}
