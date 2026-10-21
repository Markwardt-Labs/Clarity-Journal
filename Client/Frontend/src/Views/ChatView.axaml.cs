namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The Chat screen: the conversation about the journal and the box to ask a question.
/// </summary>
public sealed partial class ChatView : UserControl
{
    private ChatViewModel? model;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatView" /> class.
    /// </summary>
    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (model is not null)
        {
            model.Messages.CollectionChanged -= ScrollDown;
        }

        model = DataContext as ChatViewModel;

        if (model is not null)
        {
            model.Messages.CollectionChanged += ScrollDown;
        }
    }

    private void ScrollDown(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Dispatcher.UIThread.Post(() => this.FindControl<ScrollViewer>("Scroller")?.ScrollToEnd(), DispatcherPriority.Background);
}
