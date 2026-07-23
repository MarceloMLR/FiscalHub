namespace FiscalHub.Adapters.Ingress.BlobDrop.Tests;

/// <summary>
/// Especifica a convenção de nomes da zona de drop: o gatilho deriva tenant e chave do nome do
/// arquivo, sem abrir o XML (agnóstico de formato).
/// </summary>
public class DropBlobNamingTests
{
    [Fact]
    public void Parses_tenant_and_key_from_folder_style_name()
    {
        var (tenant, key) = DropBlobNaming.Parse("tenant-b/nfe-600.xml", "tenant-a");

        Assert.Equal("tenant-b", tenant);
        Assert.Equal("nfe-600", key);
    }

    [Fact]
    public void Uses_default_tenant_when_name_has_no_folder()
    {
        var (tenant, key) = DropBlobNaming.Parse("nfe-600.xml", "tenant-a");

        Assert.Equal("tenant-a", tenant);
        Assert.Equal("nfe-600", key);
    }

    [Fact]
    public void Handles_name_without_extension()
    {
        var (tenant, key) = DropBlobNaming.Parse("tenant-b/nfe-600", "tenant-a");

        Assert.Equal("tenant-b", tenant);
        Assert.Equal("nfe-600", key);
    }
}
