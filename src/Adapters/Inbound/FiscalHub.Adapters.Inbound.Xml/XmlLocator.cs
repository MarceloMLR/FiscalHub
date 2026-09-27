using FiscalHub.Application.Inbound;

namespace FiscalHub.Adapters.Inbound.Xml;

/// <summary>
/// A regra do locator do XML (ADR-0028). O locator é <c>container/blob</c>, e o leitor de blob alcança qualquer
/// container da conta. Por isso ele MUST apontar para o espaço de entrada do próprio tenant da referência,
/// <c>{inbox}/{tenant}/{arquivo}</c>; senão, um usuário faria o hub montar, sob o tenant dele, o documento de
/// outro — a leitura alheia lavada pela própria esteira.
/// </summary>
internal static class XmlLocator
{
    /// <summary>Container de entrada padrão: o mesmo <c>InboxContainer</c> para onde o drop move os arquivos.</summary>
    public const string DefaultInboxContainer = "nfe";

    // O armazenamento das fotos (ADR-0006). Trace é saída, e não entrada.
    private const string TraceContainer = "traces";

    /// <summary>O problema do locator, ou <c>null</c> se ele vale. Não toca o armazenamento.</summary>
    public static string? Check(DocumentReference reference, string inboxContainer)
    {
        string locator = reference.Locator;
        if (string.IsNullOrWhiteSpace(locator))
        {
            return "o locator está vazio.";
        }

        // O System.Uri normaliza "." e ".." ao montar a URI do blob: "nfe/tenant-b/../tenant-a/x.xml" passaria na
        // conferência de prefixo feita no texto e seria lido como "nfe/tenant-a/x.xml". Nada disso é aceito.
        string[] segments = locator.Split('/');
        if (locator.Contains('\\') || segments.Any(s => s.Length == 0 || s is "." or ".."))
        {
            return $"o locator '{locator}' tem barra invertida, segmento vazio, '.' ou '..'.";
        }

        // Regra própria, antes da do prefixo: vale mesmo que um dia a do prefixo seja afrouxada.
        if (string.Equals(segments[0], TraceContainer, StringComparison.OrdinalIgnoreCase))
        {
            return "o armazenamento de fotos (traces) nunca é origem de ingestão.";
        }

        string expected = $"{inboxContainer}/{reference.TenantId}/<arquivo>";
        if (!string.Equals(segments[0], inboxContainer, StringComparison.Ordinal))
        {
            return $"o locator precisa estar no container de entrada '{inboxContainer}' ({expected}).";
        }

        if (segments.Length < 3 || !string.Equals(segments[1], reference.TenantId, StringComparison.Ordinal))
        {
            return $"o locator precisa estar no espaço do tenant: {expected}.";
        }

        return null;
    }
}
