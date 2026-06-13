namespace Argent.Models.DataSources;

public class SqlDataSource : DataSource
{
    public override DataSourceKind Kind => DataSourceKind.Sql;

    /// <summary>Secret — encrypted at rest by the catalog.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
