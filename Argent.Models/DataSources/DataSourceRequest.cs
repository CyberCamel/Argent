using System.Text.Json.Serialization;

namespace Argent.Models.DataSources;

/// <summary>
/// What to fetch from a connection. Carried by the consumer (since data sources are pure
/// connections) and paired with the matching connection kind by the provider. Tokens of the
/// form <c>{{name}}</c> (REST/SOAP) and <c>@name</c> (SQL parameters) are bound at call time.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SqlRequest), "sql")]
[JsonDerivedType(typeof(RestRequest), "rest")]
[JsonDerivedType(typeof(SoapRequest), "soap")]
public abstract class DataSourceRequest
{
    [JsonIgnore]
    public abstract DataSourceKind Kind { get; }
}

public class SqlRequest : DataSourceRequest
{
    public override DataSourceKind Kind => DataSourceKind.Sql;

    public string Query { get; set; } = string.Empty;
    /// <summary>Static default parameters; runtime parameters override these by name.</summary>
    public Dictionary<string, object?> Parameters { get; set; } = [];
    public bool UseTransaction { get; set; }
}

public class RestRequest : DataSourceRequest
{
    public override DataSourceKind Kind => DataSourceKind.Rest;

    public string Method { get; set; } = "GET";
    /// <summary>Appended to the connection's BaseUrl (or absolute). May contain {{tokens}}.</summary>
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Query { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? Body { get; set; }

    /// <summary>Dotted path to the array of rows in a JSON response (e.g. "data.items"). Null = response root.</summary>
    public string? RowsPath { get; set; }
}

public class SoapRequest : DataSourceRequest
{
    public override DataSourceKind Kind => DataSourceKind.Soap;

    /// <summary>SOAPAction header value.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Full SOAP envelope XML. May contain {{tokens}}.</summary>
    public string Envelope { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = [];
}
