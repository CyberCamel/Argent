namespace Argent.Models.DataSources;

public class RestDataSource : DataSource
{
    public override DataSourceKind Kind => DataSourceKind.Rest;

    public string BaseUrl { get; set; } = string.Empty;
    public DataSourceAuthType AuthType { get; set; } = DataSourceAuthType.None;

    public string? ApiKeyHeader { get; set; }
    public string? ApiKeyValue { get; set; }   // secret
    public string? Username { get; set; }
    public string? Password { get; set; }       // secret
    public string? BearerToken { get; set; }    // secret

    public Dictionary<string, string> DefaultHeaders { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 30;
}
