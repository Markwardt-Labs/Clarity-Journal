namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Renders markdown text, including lists, quotes, tables and code blocks, as native Avalonia controls.
/// </summary>
internal sealed class MarkdownView : ContentControl
{
    /// <summary>
    /// Identifies the <see cref="Source" /> property.
    /// </summary>
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<MarkdownView, string?>(nameof(Source));

    static MarkdownView() => SourceProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.Render());

    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private readonly FontFamily mono = new("Menlo, Consolas, DejaVu Sans Mono, monospace");

    /// <summary>
    /// Gets or sets the markdown text to render.
    /// </summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private void Render() => Content = Source is null ? null : Blocks(Markdig.Markdown.Parse(Source, pipeline));

    private IBrush Brush(string key, IBrush fallback) => Application.Current?.Resources.TryGetResource(key, ActualThemeVariant, out object? value) == true && value is IBrush brush ? brush : fallback;

    private StackPanel Blocks(IEnumerable<Block> blocks)
    {
        StackPanel panel = new() { Spacing = 8 };

        foreach (Block block in blocks)
        {
            if (Build(block) is { } control)
            {
                panel.Children.Add(control);
            }
        }

        return panel;
    }

    private Control? Build(Block block) => block switch
    {
        HeadingBlock heading => Text(heading.Inline, heading.Level switch { 1 => 22, 2 => 19, 3 => 17, _ => 15 }, FontWeight.Bold),
        ParagraphBlock paragraph => Text(paragraph.Inline, 15, FontWeight.Normal),
        FencedCodeBlock or CodeBlock => Code(block),
        QuoteBlock quote => Quote(quote),
        ListBlock list => List(list),
        ThematicBreakBlock => new Border { Height = 1, Background = Brush("Lilac", Brushes.Gray), Margin = new Thickness(0, 4) },
        Table table => Tabular(table),
        ContainerBlock container => Blocks(container),
        _ => null
    };

    private TextBlock Text(ContainerInline? inline, double size, FontWeight weight)
    {
        TextBlock text = new() { FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45 };

        if (inline is not null)
        {
            Inlines(text.Inlines!, inline);
        }

        return text;
    }

    private Border Code(Block block)
    {
        string code = block is LeafBlock leaf ? leaf.Lines.ToString() : string.Empty;
        return new Border
        {
            Background = Brush("Lilac", Brushes.LightGray),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8),
            Child = new TextBlock { Text = code.TrimEnd(), FontFamily = mono, FontSize = 13, TextWrapping = TextWrapping.Wrap }
        };
    }

    private Border Quote(QuoteBlock quote) => new()
    {
        BorderBrush = Brush("Rose", Brushes.HotPink),
        BorderThickness = new Thickness(3, 0, 0, 0),
        Padding = new Thickness(12, 0, 0, 0),
        Child = Blocks(quote)
    };

    private StackPanel List(ListBlock list)
    {
        StackPanel panel = new() { Spacing = 4 };
        int number = int.TryParse(list.OrderedStart, CultureInfo.InvariantCulture, out int start) ? start : 1;

        foreach (ListItemBlock item in list.OfType<ListItemBlock>())
        {
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•", FontSize = 15, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top });
            StackPanel content = Blocks(item);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private Grid Tabular(Table table)
    {
        Grid grid = new() { ColumnSpacing = 14, RowSpacing = 4 };
        int columns = table.OfType<TableRow>().Max(row => row.Count);

        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        int index = 0;

        foreach (TableRow row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int position = 0;

            foreach (TableCell cell in row.OfType<TableCell>())
            {
                TextBlock text = cell.FirstOrDefault() is LeafBlock { Inline: { } inline } ? Text(inline, 14, row.IsHeader ? FontWeight.Bold : FontWeight.Normal) : new TextBlock();
                Grid.SetRow(text, index);
                Grid.SetColumn(text, position++);
                grid.Children.Add(text);
            }

            index++;
        }

        return grid;
    }

    private void Inlines(InlineCollection target, ContainerInline container)
    {
        foreach (Markdig.Syntax.Inlines.Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = mono, Background = Brush("Lilac", Brushes.LightGray) });
                    break;
                case LineBreakInline lineBreak:
                    target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
                    break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterChar == '~' ? new Span { TextDecorations = TextDecorations.Strikethrough } : emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    Inlines(span.Inlines, emphasis);
                    target.Add(span);
                    break;
                case LinkInline link:
                    Span linked = new() { Foreground = Brush("Violet", Brushes.MediumPurple), TextDecorations = TextDecorations.Underline };
                    Inlines(linked.Inlines, link);
                    target.Add(linked);
                    break;
                case ContainerInline nested:
                    Inlines(target, nested);
                    break;
            }
        }
    }
}
