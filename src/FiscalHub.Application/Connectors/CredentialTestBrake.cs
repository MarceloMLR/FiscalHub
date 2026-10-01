using System.Collections.Concurrent;

namespace FiscalHub.Application.Connectors;

public sealed class CredentialTestOptions
{
    /// <summary>Quanto tempo um teste recusado fica lembrado: o mesmo intervalo da recusa lembrada da Avalara.</summary>
    public TimeSpan RefusalHold { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// O freio do botão de teste (change <c>module-navigation-and-integration-panel</c>, D12): lembra um teste recusado por
/// <see cref="CredentialTestOptions.RefusalHold"/>, por tenant e por adapter, e, na saída, por ambiente, porque Sandbox e
/// Produção são duas credenciais.
/// <para>Por que no botão, e não no provedor de token do D365: o coletor tenta uma vez por intervalo, e uma pessoa clica
/// quantas vezes quiser. E cada teste é um pedido real ao emissor do token, porque nenhum reusa cache.</para>
/// <para>Lembra só a recusa. Um sucesso lembrado seria o teste que mente, só que no freio. Salvar o perfil esquece o freio
/// do tenant, porque a correção pode ter sido feita do outro lado. Em memória, por réplica, como a recusa da Avalara.</para>
/// </summary>
public sealed class CredentialTestBrake : IConnectorProfileObserver
{
    private readonly ConcurrentDictionary<Key, Refusal> _refusals = new();
    private readonly CredentialTestOptions _options;
    private readonly TimeProvider _clock;

    public CredentialTestBrake(CredentialTestOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    /// <summary>A recusa lembrada, se ainda vale.</summary>
    public bool TryHeld(string tenantId, string adapter, string? environment, out string reason, out DateTimeOffset until)
    {
        var key = new Key(tenantId, adapter, environment);
        if (_refusals.TryGetValue(key, out Refusal? refusal))
        {
            if (refusal.Until > _clock.GetUtcNow())
            {
                (reason, until) = (refusal.Reason, refusal.Until);
                return true;
            }

            _refusals.TryRemove(new KeyValuePair<Key, Refusal>(key, refusal));
        }

        (reason, until) = (string.Empty, default);
        return false;
    }

    /// <summary>Lembra a recusa, e devolve até quando ela vale.</summary>
    public DateTimeOffset Remember(string tenantId, string adapter, string? environment, string reason)
    {
        DateTimeOffset until = _clock.GetUtcNow() + _options.RefusalHold;
        _refusals[new Key(tenantId, adapter, environment)] = new Refusal(reason, until);
        return until;
    }

    public Task ProfileSavedAsync(string tenantId, CancellationToken ct = default)
    {
        foreach (Key key in _refusals.Keys.Where(k => k.TenantId == tenantId))
        {
            _refusals.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    private sealed record Key(string TenantId, string Adapter, string? Environment);

    private sealed record Refusal(string Reason, DateTimeOffset Until);
}
