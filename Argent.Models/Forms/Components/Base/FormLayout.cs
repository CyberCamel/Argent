using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Base;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LayoutType
{
    Flex,
    Grid,
    Fieldset,
    Tabs,
    Accordion
}

public class FormLayout : FormComponent
{
    [JsonPropertyName("layout")]
    public LayoutType LayoutType { get; set; } = LayoutType.Flex;

    [JsonPropertyName("items")]
    public List<FormComponent> Items { get; set; } = [];

    [JsonPropertyName("columns")]
    public int Columns { get; set; } = 1;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "row";

    [JsonPropertyName("wrap")]
    public bool Wrap { get; set; } = true;

    [JsonPropertyName("align")]
    public string? Align { get; set; }

    [JsonPropertyName("justify")]
    public string? Justify { get; set; }

    [JsonPropertyName("gap")]
    public int Gap { get; set; } = 3;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Raw HTML content for HtmlBox containers.</summary>
    [JsonPropertyName("html")]
    public string? Html { get; set; }

    [JsonPropertyName("collapsible")]
    public bool Collapsible { get; set; }

    [JsonPropertyName("defaultOpen")]
    public bool DefaultOpen { get; set; } = true;

    [JsonPropertyName("activeItem")]
    public int ActiveItem { get; set; } = 0;

    [JsonPropertyName("multiExpand")]
    public bool MultiExpand { get; set; }
}