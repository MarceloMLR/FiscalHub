namespace FiscalHub.Domain.Envelope;

/// <summary>Tipo de documento fiscal transportado pela esteira.</summary>
public enum DocumentType
{
    /// <summary>NF-e de mercadoria (modelo 55).</summary>
    GoodsInvoice55,

    /// <summary>CT-e de transporte (modelo 57).</summary>
    Transport57,

    /// <summary>NFS-e de serviço.</summary>
    ServiceNfse,
}
