namespace FiscalHub.Adapters.Ingress.BlobDrop;

/// <summary>
/// Convenção de nomes da zona de drop: exatamente <c>{tenant}/{chave}.xml</c> → (tenant, chave). O gatilho é agnóstico
/// de formato: não abre o XML, deriva a chave do nome. Não existe tenant padrão (ADR-0028): um arquivo fora do formato
/// não é de tenant nenhum, e não é ingerido.
/// </summary>
internal static class DropBlobNaming
{
    public static bool TryParse(string blobName, out string tenant, out string key)
    {
        string withoutExtension = blobName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            ? blobName[..^4]
            : blobName;

        string[] segments = withoutExtension.Split('/');
        bool valid = segments.Length == 2 && segments[0].Length > 0 && segments[1].Length > 0;

        tenant = valid ? segments[0] : string.Empty;
        key = valid ? segments[1] : string.Empty;
        return valid;
    }
}

/// <summary>
/// Os arquivos do drop que não seguem o formato já avisados neste processo. O arquivo fica na zona de drop, e o aviso
/// sai uma vez por nome, e não a cada varredura.
/// </summary>
internal sealed class DropWarnings
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public bool FirstSighting(string blobName) => _seen.Add(blobName);
}
