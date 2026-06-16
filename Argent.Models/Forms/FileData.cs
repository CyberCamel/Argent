namespace Argent.Models.Forms;

public record FileData
{
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "";
    public long Size { get; init; }
    public string Base64Content { get; init; } = "";
}
