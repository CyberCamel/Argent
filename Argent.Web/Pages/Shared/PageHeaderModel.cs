namespace Argent.Web.Pages.Shared
{
    public class PageHeaderModel
    {
        public required string Title { get; set; }
        public string? Subtitle { get; set; }
        public LinkModel? ActionLink { get; set; }
        public ModalModel? ActionModal { get; set; }
    }

    public record LinkModel(string Label, string Url, string? Icon = null);
    public record ModalModel(string Label, string ModalId, string? Icon = null);

}
