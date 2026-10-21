namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The summary screen: the week, month, year and life summaries the server wrote.
/// </summary>
internal sealed class SummariesViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SummariesViewModel" /> class.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    public SummariesViewModel(IServerConnection connection)
    {
        this.connection = connection;
        Kinds =
        [
            new SummaryKindChoice(SummaryKind.Week, "Week", new AsyncCommand(_ => Select(SummaryKind.Week))) { IsSelected = true },
            new SummaryKindChoice(SummaryKind.Month, "Month", new AsyncCommand(_ => Select(SummaryKind.Month))),
            new SummaryKindChoice(SummaryKind.Year, "Year", new AsyncCommand(_ => Select(SummaryKind.Year))),
            new SummaryKindChoice(SummaryKind.Lifetime, "Life", new AsyncCommand(_ => Select(SummaryKind.Lifetime)))
        ];
    }

    private readonly IServerConnection connection;

    private SummaryKind kind = SummaryKind.Week;

    /// <summary>
    /// Gets the segments that pick the kind of summary.
    /// </summary>
    public IReadOnlyList<SummaryKindChoice> Kinds { get; }

    /// <summary>
    /// Gets the summaries of the selected kind, newest period first.
    /// </summary>
    public ObservableCollection<SummaryItemViewModel> Items { get; } = [];

    /// <summary>
    /// Reads the summaries of the selected kind.
    /// </summary>
    /// <returns>A task that completes once they are read or the read failed.</returns>
    public Task Load() => LoadSummaries();

    /// <summary>
    /// Reacts to an event pushed by the server.
    /// </summary>
    /// <param name="remoteEvent">The event.</param>
    /// <returns>A task that completes once the screen is up to date.</returns>
    public async Task Handle(RemoteEvent remoteEvent)
    {
        switch (remoteEvent.Kind)
        {
            case RemoteEventKind.SummariesChanged:
                await LoadSummaries();
                break;
            case RemoteEventKind.Failed:
                Error = remoteEvent.Error;
                break;
        }
    }

    private async Task Select(SummaryKind selected)
    {
        kind = selected;

        foreach (SummaryKindChoice choice in Kinds)
        {
            choice.IsSelected = choice.Kind == selected;
        }

        await LoadSummaries();
    }

    private async Task LoadSummaries()
    {
        SummaryKind shown = kind;
        IReadOnlyList<Summary> summaries = [];

        if (!await Attempt(async () => summaries = await connection.GetSummaries(shown, CancellationToken.None)) || shown != kind)
        {
            return;
        }

        Items.Clear();

        foreach (Summary summary in summaries)
        {
            Items.Add(new SummaryItemViewModel(summary));
        }

    }
}
