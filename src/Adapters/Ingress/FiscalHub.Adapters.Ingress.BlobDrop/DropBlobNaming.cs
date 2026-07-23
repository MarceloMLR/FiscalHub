namespace FiscalHub.Adapters.Ingress.BlobDrop;

/// <summary>
/// Convenção de nomes da zona de drop: <c>{tenant}/{chave}.xml</c> → (tenant, chave). Sem barra,
/// usa o tenant padrão. O gatilho é agnóstico de formato: não abre o XML, deriva a chave do nome.
/// </summary>
internal static class DropBlobNaming
{
    public static (string Tenant, string Key) Parse(string blobName, string defaultTenant)
    {
        string withoutExtension = blobName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            ? blobName[..^4]
            : blobName;

        int slash = withoutExtension.LastIndexOf('/');
        if (slash < 0)
        {
            return (defaultTenant, withoutExtension);
        }

        string tenant = withoutExtension[..slash];
        string key = withoutExtension[(slash + 1)..];
        return (string.IsNullOrWhiteSpace(tenant) ? defaultTenant : tenant, key);
    }
}
