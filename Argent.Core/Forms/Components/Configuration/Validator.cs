using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Core.Forms.Components.Configuration;


public enum ValidationType
{
    Expression,
    Regex,
    Required,
    Specific
}

public class Validator
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ValidationType Type { get; set; } = ValidationType.Required;
    public string? Expression { get; set; }
    public string? Pattern { get; set; }
    public string? ErrorKey { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ErrorArgs { get; set; } = [];

}
