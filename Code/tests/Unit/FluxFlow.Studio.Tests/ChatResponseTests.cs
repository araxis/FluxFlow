using FluxFlow.Studio.UI.Components.Markdown;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ChatResponseTests
{
    [Fact]
    public void Responses_format_lists_code_and_links_without_executing_markup_or_loading_images()
    {
        var html = MarkdownFormatting.ToHtml("""
            **Summary**

            - First
            - Second

            ```json
            {"KeepCASE": true}
            ```

            [Documentation](https://example.com/docs)
            [Unsafe](javascript:alert(1))
            ![Remote](https://example.com/track.png)
            <img src=x onerror=alert(1)><script>alert(1)</script>
            """);
        html.ShouldContain("<strong>Summary</strong>");
        html.ShouldContain("<ul>");
        html.ShouldContain("<pre><code>");
        html.ShouldContain("KeepCASE");
        html.ShouldContain("href=\"https://example.com/docs\"");
        html.ShouldNotContain("href=\"javascript:");
        html.ShouldNotContain("<img");
        html.ShouldNotContain("<script");
    }
}
