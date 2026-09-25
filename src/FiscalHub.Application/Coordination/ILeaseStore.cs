namespace FiscalHub.Application.Coordination;

/// <summary>
/// Lease exclusivo com prazo, por recurso, para coordenar réplicas (ex.: só uma faz o poll de um tenant
/// por vez). Genérico de propósito: o agendador pode adotá-lo depois (pendência do ADR-0017).
/// Implementada na Infrastructure.
/// </summary>
public interface ILeaseStore
{
    /// <summary>Toma o lease se está livre, expirado ou já é do próprio dono. <c>false</c> = outro dono válido.</summary>
    Task<bool> TryAcquireAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Estende o prazo se o lease ainda é do dono. <c>false</c> = outro tomou.</summary>
    Task<bool> RenewAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Libera o lease, se ainda é do dono. Não falha se já não for.</summary>
    Task ReleaseAsync(string resource, string owner, CancellationToken ct = default);
}

/// <summary>Posse de um lease (recurso + dono), apresentada a quem precisa condicionar uma gravação a ela.</summary>
public sealed record LeaseClaim(string Resource, string Owner);
