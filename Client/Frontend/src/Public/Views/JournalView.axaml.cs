namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The Journal screen: the entry editor and the list of entries.
/// </summary>
public sealed partial class JournalView : UserControl
{
    private readonly double loadAhead = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="JournalView" /> class.
    /// </summary>
    public JournalView()
        => InitializeComponent();

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scroller && DataContext is JournalViewModel model && scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - loadAhead)
        {
            _ = model.LoadMore();
        }
    }
}
