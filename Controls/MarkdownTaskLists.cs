using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;

namespace OmniLauncher.Controls;

public sealed record NoteTaskToggle(string Source, int StateOffset, bool IsChecked);

internal static class MarkdownTaskLists
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseTaskLists().UsePreciseSourceLocation().Build();

    // Parse source spans so identical task labels, nested lists and CRLF notes map
    // back to the exact character. Code blocks and escaped markers remain untouched.
    public static string Prepare(string source, Func<int, bool, string, string> createToken)
    {
        var tasks = Markdig.Markdown.Parse(source, Pipeline).Descendants<TaskList>()
            .OrderBy(task => task.Span.Start).ToArray();
        var result = new StringBuilder();
        var position = 0;
        foreach (var task in tasks)
        {
            var start = task.Span.Start;
            var end = task.Span.End + 1;
            if (start < position || end > source.Length || end - start != 3 ||
                source[start] != '[' || source[end - 1] != ']') continue;
            var lineEnd = source.IndexOf('\n', end);
            if (lineEnd < 0) lineEnd = source.Length;
            var label = source[end..lineEnd].Trim();
            result.Append(source, position, start - position);
            result.Append(createToken(start + 1, task.Checked, label));
            position = end;
        }
        result.Append(source, position, source.Length - position);
        return result.ToString();
    }
}
