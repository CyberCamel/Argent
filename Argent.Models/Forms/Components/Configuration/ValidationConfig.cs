using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Configuration;

public class ValidationConfig
{
    [JsonPropertyName("condition")]
    public Condition? Condition { get; set; }

    [JsonPropertyName("errorKey")]
    public string? ErrorKey { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("errorArgs")]
    public List<string> ErrorArgs { get; set; } = [];
}
