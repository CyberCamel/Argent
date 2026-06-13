namespace Argent.Models.DataSources;

public enum DataSourceKind
{
    Sql,
    Rest,
    Soap
}

public enum DataSourceAuthType
{
    None,
    ApiKey,
    Basic,
    Bearer
}
