using Azure.Storage.Blobs;

namespace FiscalHub.Host;

/// <summary>Seed de dev local: garante o container do Blob e sobe um XML de NF-e de exemplo.</summary>
internal static class LocalSeed
{
    public const string Container = "nfe";
    public const string BlobName = "nfe-exemplo.xml";

    public static string Locator => $"{Container}/{BlobName}";

    public static async Task RunAsync(IServiceProvider services)
    {
        var blobService = services.GetRequiredService<BlobServiceClient>();
        BlobContainerClient container = blobService.GetBlobContainerClient(Container);
        await container.CreateIfNotExistsAsync();

        await container.GetBlobClient(BlobName).UploadAsync(BinaryData.FromString(SampleXml), overwrite: true);
    }

    private const string SampleXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
          <NFe>
            <infNFe Id="NFe35260612345678000190550010000001231000000123" versao="4.00">
              <ide>
                <mod>55</mod>
                <serie>1</serie>
                <nNF>123</nNF>
                <dhEmi>2026-06-01T10:00:00-03:00</dhEmi>
                <cMunFGIBS>3550308</cMunFGIBS>
              </ide>
              <emit>
                <CNPJ>12345678000190</CNPJ>
                <xNome>Empresa Emitente LTDA</xNome>
                <IE>123456789</IE>
              </emit>
              <dest>
                <CNPJ>98765432000110</CNPJ>
                <xNome>Cliente Destinatario SA</xNome>
              </dest>
              <det nItem="1">
                <prod>
                  <cProd>PROD-001</cProd>
                  <xProd>Produto de Teste</xProd>
                  <NCM>12345678</NCM>
                  <CFOP>5102</CFOP>
                  <qCom>2.0000</qCom>
                  <vUnCom>50.0000</vUnCom>
                  <vProd>100.00</vProd>
                </prod>
                <imposto>
                  <IBSCBS>
                    <CST>000</CST>
                    <cClassTrib>000001</cClassTrib>
                    <gIBSCBS>
                      <vBC>100.00</vBC>
                      <gIBSUF>
                        <pIBSUF>8.50</pIBSUF>
                        <vIBSUF>8.50</vIBSUF>
                      </gIBSUF>
                      <gIBSMun>
                        <pIBSMun>2.00</pIBSMun>
                        <vIBSMun>2.00</vIBSMun>
                      </gIBSMun>
                      <vIBS>10.50</vIBS>
                      <gCBS>
                        <pCBS>0.90</pCBS>
                        <vCBS>0.90</vCBS>
                      </gCBS>
                    </gIBSCBS>
                  </IBSCBS>
                </imposto>
              </det>
              <total>
                <ICMSTot>
                  <vNF>100.00</vNF>
                </ICMSTot>
              </total>
            </infNFe>
          </NFe>
        </nfeProc>
        """;
}
