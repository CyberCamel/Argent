using Argent.Models.Forms.Components.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Base
{
    public class FormInputComponent : FormComponent
    {
        public required string DataKey { get; set; }
        public string? Label { get; set; }
        public string? LabelKey { get; set; }
        public object? DefaultValue { get; set; }
        public string? RequiredIf { get; set; }
        public string? Formula { get; set; }
        public bool IsSensitive { get; set; } = false;
        public DataProviderConfig? DataProvider { get; set; }
        public List<ValidationConfig> Validators { get; set; } = [];
    }
}
