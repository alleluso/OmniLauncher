using System.Windows.Input;
using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using CommunityToolkit.Mvvm.Input;
using Markdown.Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Threading;
using ColorTextBlock.Avalonia;
using Markdown.Avalonia.Parsers;
using Markdown.Avalonia.Plugins;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Avalonia.VisualTree;

namespace OmniLauncher.Controls;

public sealed class NotesMarkdownPreview : MarkdownScrollViewer
{
    public static readonly StyledProperty<string> SourceMarkdownProperty =
        AvaloniaProperty.Register<NotesMarkdownPreview, string>(nameof(SourceMarkdown), "");
    public static readonly StyledProperty<ICommand?> ToggleTaskCommandProperty =
        AvaloniaProperty.Register<NotesMarkdownPreview, ICommand?>(nameof(ToggleTaskCommand));
    private readonly Dictionary<string, CheckBox> _tasks = new();

    public string SourceMarkdown
    {
        get => GetValue(SourceMarkdownProperty);
        set => SetValue(SourceMarkdownProperty, value);
    }
    public ICommand? ToggleTaskCommand
    {
        get => GetValue(ToggleTaskCommandProperty);
        set => SetValue(ToggleTaskCommandProperty, value);
    }
    public static readonly StyledProperty<ICommand?> OpenLinkCommandProperty =
        AvaloniaProperty.Register<NotesMarkdownPreview, ICommand?>(nameof(OpenLinkCommand));

    public ICommand? OpenLinkCommand
    {
        get => GetValue(OpenLinkCommandProperty);
        set => SetValue(OpenLinkCommandProperty, value);
    }

    public NotesMarkdownPreview()
    {
        Styles.Add(new StyleInclude(new Uri("avares://OmniLauncher/"))
        { Source = new Uri("avares://OmniLauncher/Styles/MarkdownNotesStyles.axaml") });
        // Route links through the same URL validation and error handling as saved project links.
        var engine = new Markdown.Avalonia.Markdown
        {
            HyperlinkCommand = new RelayCommand<string>(url =>
            {
                if (OpenLinkCommand?.CanExecute(url) == true) OpenLinkCommand.Execute(url);
            })
        };
        Engine = engine;
        Plugins.Plugins.Add(new TaskPlugin(_tasks));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SourceMarkdownProperty) return;
        var source = SourceMarkdown;
        var scroll = ScrollValue;
        _tasks.Clear();
        var prefix = "\uE000" + Guid.NewGuid().ToString("N") + ":";
        Markdown = MarkdownTaskLists.Prepare(source, (offset, isChecked, label) =>
        {
            var token = prefix + offset + "\uE001";
            var box = new CheckBox
            {
                IsChecked = isChecked, MinHeight = 24, MinWidth = 24,
                Padding = new Thickness(0), Tag = offset,
                Margin = new Thickness(0, 0, 2, 0)
            };
            box.AttachedToVisualTree += (_, _) => HideTaskBullet(box);
            AutomationProperties.SetName(box, string.IsNullOrEmpty(label) ? "Task" : label);
            box.IsCheckedChanged += (_, _) =>
            {
                var toggle = new NoteTaskToggle(source, offset, box.IsChecked == true);
                if (ToggleTaskCommand?.CanExecute(toggle) == true)
                {
                    // Defer rebuilding the document until the input event has finished.
                    var restoreFocus = box.IsFocused;
                    Dispatcher.UIThread.Post(() =>
                    {
                        ToggleTaskCommand?.Execute(toggle);
                        if (restoreFocus)
                            _tasks.Values.FirstOrDefault(task => Equals(task.Tag, offset))?.Focus();
                    });
                }
            };
            _tasks[token] = box;
            return token;
        });
        Dispatcher.UIThread.Post(() =>
        {
            if (SourceMarkdown == source) ScrollValue = scroll;
        }, DispatcherPriority.Loaded);
    }

    private static void HideTaskBullet(CheckBox box)
    {
        // Align every inline in the task paragraph, not just the checkbox. The
        // renderer otherwise aligns text to its baseline and the control to center.
        var paragraph = box.GetVisualAncestors().OfType<CTextBlock>().FirstOrDefault();
        if (paragraph is not null)
            paragraph.TextVerticalAlignment = TextVerticalAlignment.Center;
        // A list grid pairs each item with a marker in column zero. Only hide the
        // marker belonging to this task; ordinary items in mixed lists keep theirs.
        foreach (var ancestor in box.GetVisualAncestors().OfType<Control>())
        {
            if (ancestor.GetVisualParent() is not Grid grid || !grid.Classes.Contains("List")) continue;
            var row = Grid.GetRow(ancestor);
            foreach (var marker in grid.Children.Where(child => Grid.GetRow(child) == row &&
                         Grid.GetColumn(child) == 0 && child.Classes.Contains("ListMarker")))
                marker.IsVisible = false;
            break;
        }
    }

    private sealed class TaskPlugin(Dictionary<string, CheckBox> tasks) : IMdAvPlugin
    {
        public void Setup(SetupInfo info) => info.Register(InlineParser.New(
            new Regex("\uE000[a-f0-9]{32}:[0-9]+\uE001"), "NoteTaskCheckbox",
            match => tasks.TryGetValue(match.Value, out var box)
                ? (CInline)new CInlineUIContainer(box) { TextVerticalAlignment = TextVerticalAlignment.Center }
                : new CRun { Text = match.Value }));
    }
}
