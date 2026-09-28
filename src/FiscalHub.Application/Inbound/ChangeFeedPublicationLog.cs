using System.Collections.Concurrent;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Pares (documento, carimbo de alteração) que esta réplica já publicou com o carimbo assentado, por
/// (tenant, origem) — é o que deixa o <see cref="ChangeFeedPoller"/> não republicar o repetido da
/// sobreposição (ADR-0025, design D16). Filtro de tráfego, não garantia: perder o registro (restart,
/// troca de réplica) só devolve a repetição; a garantia contra reenvio é o hash por conteúdo (ADR-0016).
/// Em memória e singleton — o poller é recriado a cada tick e não pode guardá-lo.
/// </summary>
public sealed class ChangeFeedPublicationLog
{
    private readonly ConcurrentDictionary<(string Tenant, string Origin), Partition> _partitions = new();

    /// <summary>
    /// Prepara a leitura de um (tenant, origem): zera o registro se a marca regrediu desde a última vista
    /// (rebobinamento — quem rebobina quer tudo de volta na fila) e esquece os pares que a consulta
    /// <c>gt since</c> não devolve mais.
    /// </summary>
    public void BeginPull(string tenantId, string origin, DateTimeOffset watermark, DateTimeOffset since)
    {
        Partition partition = Get(tenantId, origin);
        lock (partition)
        {
            if (partition.LastWatermark is { } last && watermark < last)
            {
                partition.Pairs.Clear();
            }

            partition.LastWatermark = watermark;
            partition.Pairs.RemoveWhere(p => p.ChangedAt <= since);
        }
    }

    /// <summary>
    /// Esquece tudo de um (tenant, origem), os pares e a última marca vista, como numa partição nova. O poller chama
    /// quando o cursor não tem marca (apagado para o <c>startFrom</c> valer de novo, ou recriado sem marca por uma falha):
    /// a marca que vai nascer pode ser igual à última vista, e aí o <see cref="BeginPull"/> não reconhece o rebobinamento.
    /// </summary>
    public void Forget(string tenantId, string origin)
    {
        Partition partition = Get(tenantId, origin);
        lock (partition)
        {
            partition.Pairs.Clear();
            partition.LastWatermark = null;
        }
    }

    /// <summary>Anota o avanço da marca, para um rebobinamento posterior ser reconhecido.</summary>
    public void Advanced(string tenantId, string origin, DateTimeOffset watermark)
    {
        Partition partition = Get(tenantId, origin);
        lock (partition)
        {
            if (partition.LastWatermark is null || watermark > partition.LastWatermark)
            {
                partition.LastWatermark = watermark;
            }
        }
    }

    /// <summary>O par já foi publicado por esta réplica, com o carimbo assentado?</summary>
    public bool WasPublished(string tenantId, string origin, string naturalKey, DateTimeOffset changedAt)
    {
        Partition partition = Get(tenantId, origin);
        lock (partition)
        {
            return partition.Pairs.Contains((naturalKey, changedAt));
        }
    }

    /// <summary>Registra um par publicado. Só o assentado deve entrar — quem decide é o poller.</summary>
    public void Record(string tenantId, string origin, string naturalKey, DateTimeOffset changedAt)
    {
        Partition partition = Get(tenantId, origin);
        lock (partition)
        {
            partition.Pairs.Add((naturalKey, changedAt));
        }
    }

    private Partition Get(string tenantId, string origin) => _partitions.GetOrAdd((tenantId, origin), _ => new Partition());

    private sealed class Partition
    {
        public HashSet<(string NaturalKey, DateTimeOffset ChangedAt)> Pairs { get; } = [];

        public DateTimeOffset? LastWatermark { get; set; }
    }
}
