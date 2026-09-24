namespace Argent.Core.DataSources;

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
