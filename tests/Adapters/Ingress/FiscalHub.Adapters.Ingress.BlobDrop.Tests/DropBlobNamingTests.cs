namespace FiscalHub.Adapters.Ingress.BlobDrop.Tests;

/// <summary>
/// Especifica a convenção de nomes da zona de drop: o gatilho deriva tenant e chave do nome do arquivo, sem abrir o
/// XML (agnóstico de formato). O nome é exatamente <c>{tenant}/{chave}.xml</c>, e não existe tenant padrão
/// (ADR-0028): arquivo fora do formato não é ingerido, em tenant nenhum.
/// </summary>
public class DropBlobNamingTests
{
    [Fact]
    public void Parses_tenant_and_key_from_folder_style_name()
    {
        Assert.True(DropBlobNaming.TryParse("tenant-b/nfe-600.xml", out string tenant, out string key));

        Assert.Equal("tenant-b", tenant);
        Assert.Equal("nfe-600", key);
    }

    [Fact]
    public void Handles_name_without_extension()
    {
        Assert.True(DropBlobNaming.TryParse("tenant-b/nfe-600", out string tenant, out string key));

        Assert.Equal("tenant-b", tenant);
        Assert.Equal("nfe-600", key);
    }

    [Fact]
    public void File_at_the_root_has_no_tenant_and_is_not_ingested()
    {
        // Antes, a raiz caía no tenant padrão (tenant-a): "tenant nulo cai no de dev", no caminho de produção.
        Assert.False(DropBlobNaming.TryParse("nfe-600.xml", out _, out _));
    }

    [Theory]
    [InlineData("tenant-a/sub/nfe-600.xml")]
    [InlineData("/nfe-600.xml")]
    [InlineData("tenant-b/.xml")]
    [InlineData("tenant-b/")]
    public void Name_outside_the_two_segment_format_is_not_ingested(string name)
    {
        Assert.False(DropBlobNaming.TryParse(name, out _, out _));
    }

    [Fact]
    public void Unroutable_file_is_warned_about_once_per_name()
    {
        var warnings = new DropWarnings();

        Assert.True(warnings.FirstSighting("nfe-600.xml"));    // o watcher avisa
        Assert.False(warnings.FirstSighting("nfe-600.xml"));   // e não repete a cada varredura
        Assert.True(warnings.FirstSighting("nfe-601.xml"));
    }
}
