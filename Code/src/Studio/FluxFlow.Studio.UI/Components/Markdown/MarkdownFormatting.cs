using Ganss.Xss;
using Markdig;

namespace FluxFlow.Studio.UI.Components.Markdown;

internal static class MarkdownFormatting
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml().UsePipeTables().Build();

    public static string ToHtml(string text)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "br", "strong", "em", "del", "blockquote",
            "ul", "ol", "li", "pre", "code", "h1", "h2", "h3", "h4", "h5", "h6",
            "hr", "a", "table", "thead", "tbody", "tr", "th", "td"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "title"]);
        return sanitizer.Sanitize(Markdig.Markdown.ToHtml(text, Pipeline));
    }
}
