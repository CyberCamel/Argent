using Argent.Models.Forms.Components.Configuration;
using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Base;

public class FormField : FormComponent
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("fieldLabel")]
    public string? FieldLabel { get; set; }

    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }

    [JsonPropertyName("description")]
    public new string? Description { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("allowBlank")]
    public bool AllowBlank { get; set; } = true;

    [JsonPropertyName("span")]
    public int Span { get; set; } = 12;

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    [JsonPropertyName("visibleWhen")]
    public Condition? VisibleWhen { get; set; }

    [JsonPropertyName("requiredWhen")]
    public Condition? RequiredWhen { get; set; }

    [JsonPropertyName("disabledWhen")]
    public Condition? DisabledWhen { get; set; }

    [JsonPropertyName("readOnlyWhen")]
    public Condition? ReadOnlyWhen { get; set; }

    [JsonPropertyName("requiredPermission")]
    public string? RequiredPermission { get; set; }

    [JsonPropertyName("validators")]
    public List<FieldValidator> Validators { get; set; } = [];

    [JsonPropertyName("dataProvider")]
    public DataProviderConfig? DataProvider { get; set; }

    // Field-specific props (textfield, numberfield, etc.)
    [JsonPropertyName("inputType")]
    public string? InputType { get; set; }

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }

    [JsonPropertyName("rows")]
    public int? Rows { get; set; }

    [JsonPropertyName("grow")]
    public bool Grow { get; set; }

    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }

    [JsonPropertyName("step")]
    public double? Step { get; set; }

    [JsonPropertyName("precision")]
    public int? Precision { get; set; }

    [JsonPropertyName("thousandSeparator")]
    public bool ThousandSeparator { get; set; }

    [JsonPropertyName("boxLabel")]
    public string? BoxLabel { get; set; }

    [JsonPropertyName("inputValue")]
    public object? InputValue { get; set; }

    [JsonPropertyName("uncheckedValue")]
    public object? UncheckedValue { get; set; }

    [JsonPropertyName("columns")]
    public int? Columns { get; set; }

    [JsonPropertyName("items")]
    public List<SelectOption> Items { get; set; } = [];

    [JsonPropertyName("displayField")]
    public string? DisplayField { get; set; } = "label";

    [JsonPropertyName("valueField")]
    public string? ValueField { get; set; } = "value";

    [JsonPropertyName("queryMode")]
    public string? QueryMode { get; set; } = "local";

    [JsonPropertyName("filterable")]
    public bool Filterable { get; set; }

    [JsonPropertyName("multiSelect")]
    public bool MultiSelect { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("minDate")]
    public string? MinDate { get; set; }

    [JsonPropertyName("maxDate")]
    public string? MaxDate { get; set; }

    [JsonPropertyName("showTime")]
    public bool ShowTime { get; set; }

    [JsonPropertyName("minTime")]
    public string? MinTime { get; set; }

    [JsonPropertyName("maxTime")]
    public string? MaxTime { get; set; }

    [JsonPropertyName("increment")]
    public int? Increment { get; set; }

    [JsonPropertyName("html")]
    public string? Html { get; set; }
}

public class SelectOption
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }
}