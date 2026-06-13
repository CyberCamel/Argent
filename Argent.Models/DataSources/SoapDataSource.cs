namespace Argent.Models.DataSources;

public class SoapDataSource : DataSource
{
    public override DataSourceKind Kind => DataSourceKind.Soap;

    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>SOAP supports None or Basic auth here.</summary>
    public DataSourceAuthType AuthType { get; set; } = DataSourceAuthType.None;
    public string? Username { get; set; }
    public string? Password { get; set; }   // secret

    public Dictionary<string, string> DefaultHeaders { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 30;
}
