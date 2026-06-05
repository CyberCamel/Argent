using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Configuration;


public enum ValidationType
{
    Expression,
    Regex,
    Required,
    Specific
}

public class ValidationConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ValidationType Type { get; set; } = ValidationType.Required;

    
    public string? Expression { get; set; }// For Expression
    public string? Pattern { get; set; }// For Regex
    public string? Handler { get; set; } // For Specific
    public string? ErrorKey { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ErrorArgs { get; set; } = [];

}
