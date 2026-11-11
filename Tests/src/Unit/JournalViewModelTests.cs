namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class JournalViewModelTests
{
    private readonly Mock<IServerConnection> connection = new();

    private readonly DateOnly today = new(2026, 10, 16);

    private readonly Dictionary<DateOnly, JournalEntry> stored = [];

    private readonly List<EntryDigest> digests = [];

    private readonly JournalViewModel model;

    public JournalViewModelTests()
    {
        connection.Setup(target => target.GetEntry(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, CancellationToken _) => stored.GetValueOrDefault(date));
        connection.Setup(target => target.ListDigests(It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly? before, int limit, CancellationToken _) => (IReadOnlyList<EntryDigest>)[.. digests.Where(digest => before is null || digest.Date < before).OrderByDescending(digest => digest.Date).Take(limit)]);
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, string text, CancellationToken _) =>
        {
            JournalEntry entry = Entry(date, text);
            stored[date] = entry;
            return entry;
        });
        model = new JournalViewModel(connection.Object, () => today);
    }

    private DateOnly Day(string date)
        => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    private JournalEntry Entry(DateOnly date, string text)
        => new() { Date = date, Text = text, UpdatedAt = new DateTimeOffset(2026, 10, 16, 21, 5, 0, TimeSpan.Zero) };

    private void Past(string date, string text, string? title = null, string? summary = null)
    {
        DateOnly day = Day(date);
        stored[day] = Entry(day, text);
        digests.Add(new EntryDigest { Date = day, Title = title, Summary = summary, Excerpt = text });
    }

    private EntryRowViewModel Row(string date)
        => model.Rows.Single(row => row.Date == Day(date));

    private async Task OpenList()
    {
        await model.Load();
        model.ToggleListCommand.Execute(null);
    }

    [Fact]
    public async Task Load_ShowsTodaysEditableEntryWithTheListClosed()
    {
        stored[today] = Entry(today, "Today's words");

        await model.Load();

        Assert.False(model.IsListOpen);
        Assert.True(model.IsEntryOpen);
        Assert.Equal(today, model.SelectedDate);
        Assert.Equal("Friday, October\u00A016", model.SelectedTitle);
        Assert.Equal("Today's words", model.EntryText);
        Assert.True(model.CanEdit);
        Assert.False(model.IsReadOnly);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public async Task Load_NoEntryToday_ShowsAnEmptyEditor()
    {
        await model.Load();

        Assert.Equal(string.Empty, model.EntryText);
        Assert.False(model.CanSave);
        Assert.True(model.CanEdit);
    }

    [Fact]
    public async Task Load_AfterAReconnect_KeepsTextTypedAndNotYetSentInsteadOfTheServersOlderText()
    {
        stored[today] = Entry(today, "What the server has");
        await model.Load();
        model.EntryText = "What I typed while offline";

        await model.Load();

        Assert.Equal("What I typed while offline", model.EntryText);
        Assert.True(model.IsDirty);
    }

    [Fact]
    public async Task Load_ConnectionFails_ShowsTheError()
    {
        connection.Setup(target => target.GetEntry(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("The connection to the server was lost."));

        await model.Load();

        Assert.Equal("The connection to the server was lost.", model.Error);
    }

    [Fact]
    public async Task ToggleList_FlipsBetweenTheEntryAndTheListNeverShowingBoth()
    {
        await model.Load();
        Assert.True(model.IsEntryOpen);
        Assert.False(model.IsListOpen);

        model.ToggleListCommand.Execute(null);
        Assert.True(model.IsListOpen);
        Assert.False(model.IsEntryOpen);

        model.ToggleListCommand.Execute(null);
        Assert.False(model.IsListOpen);
        Assert.True(model.IsEntryOpen);
    }

    [Fact]
    public async Task List_ShowsTheDaysNewestFirstWithTheirTitleAndSummary()
    {
        Past("2026-10-12", "Monday text", "Slow Monday", "You rested and read.");
        Past("2026-10-15", "Thursday text", "Pasta night", "You cooked for friends.");
        Past("2026-10-14", "Wednesday text", "A run", "You ran 5k.");

        await OpenList();

        Assert.Equal([Day("2026-10-16"), Day("2026-10-15"), Day("2026-10-14"), Day("2026-10-12")], model.Rows.Select(row => row.Date));
        EntryRowViewModel pasta = Row("2026-10-15");
        Assert.Equal("Thu, Oct 15", pasta.DateLabel);
        Assert.Equal("Pasta night", pasta.Heading);
        Assert.True(pasta.HasHeading);
        Assert.Equal("You cooked for friends.", pasta.Body);
    }

    [Fact]
    public async Task List_ADayWithoutATitleYetShowsTheStartOfItsTextInstead()
    {
        Past("2026-10-15", "Cooked pasta with mushrooms for friends");

        await OpenList();

        EntryRowViewModel row = Row("2026-10-15");
        Assert.False(row.HasHeading);
        Assert.Equal("Cooked pasta with mushrooms for friends", row.Body);
    }

    [Fact]
    public async Task List_TodayIsAlwaysTheFirstRowEvenWithoutAnEntry()
    {
        Past("2026-10-15", "Thursday text", "Pasta night", "You cooked for friends.");

        await OpenList();

        EntryRowViewModel first = model.Rows[0];
        Assert.True(first.IsToday);
        Assert.Equal("Today", first.DateLabel);
        Assert.False(first.HasHeading);
        Assert.Equal("Tap to write today's entry", first.Body);
        Assert.True(first.IsSelected);
    }

    [Fact]
    public async Task List_TodaysRowShowsItsEntryAndIsNotRepeated()
    {
        digests.Add(new EntryDigest { Date = today, Excerpt = "Ran 5k this morning" });
        stored[today] = Entry(today, "Ran 5k this morning");

        await OpenList();

        EntryRowViewModel only = Assert.Single(model.Rows);
        Assert.True(only.IsToday);
        Assert.Equal("Ran 5k this morning", only.Body);
    }

    [Fact]
    public async Task List_LeavesOutDaysAfterTodayAndMarksTheShownDayAndOtherYears()
    {
        Past("2025-12-31", "Last year", "New Year's Eve", "You looked back.");
        digests.Add(new EntryDigest { Date = Day("2026-10-17"), Excerpt = "From a server a day ahead" });

        await OpenList();

        Assert.DoesNotContain(model.Rows, row => row.Date == Day("2026-10-17"));
        Assert.Equal("Wed, Dec 31, 2025", Row("2025-12-31").DateLabel);
    }

    [Fact]
    public async Task OpeningTheList_SendsUnsavedTextSoTheRowShowsIt()
    {
        await model.Load();
        model.EntryText = "Typed just now";

        model.ToggleListCommand.Execute(null);

        connection.Verify(target => target.SaveEntry(today, "Typed just now", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public async Task ListFailing_ShowsTheError()
    {
        await model.Load();
        connection.Setup(target => target.ListDigests(It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));

        model.ToggleListCommand.Execute(null);

        Assert.Equal("offline", model.Error);
        Assert.Empty(model.Rows);
    }

    [Fact]
    public async Task SelectingAPastRow_ShowsItsEntryReadOnlyAndClosesTheList()
    {
        Past("2026-10-14", "Wednesday text", "A run", "You ran 5k.");
        await OpenList();

        Row("2026-10-14").Select.Execute(null);

        Assert.False(model.IsListOpen);
        Assert.True(model.IsEntryOpen);
        Assert.Equal(Day("2026-10-14"), model.SelectedDate);
        Assert.Equal("Wednesday, October\u00A014", model.SelectedTitle);
        Assert.Equal("Wednesday text", model.EntryText);
        Assert.True(model.IsReadOnly);
        Assert.False(model.CanEdit);
        Assert.False(model.CanSave);
        Assert.False(model.Dictation.Command.CanExecute(null));
        Assert.True(Row("2026-10-14").IsSelected);
        Assert.False(Row("2026-10-16").IsSelected);
    }

    [Fact]
    public async Task SelectingTodaysRowAgain_MakesTheEntryEditableAgain()
    {
        Past("2026-10-14", "Wednesday text", "A run", "You ran 5k.");
        await OpenList();
        Row("2026-10-14").Select.Execute(null);
        model.ToggleListCommand.Execute(null);

        Row("2026-10-16").Select.Execute(null);

        Assert.True(model.CanEdit);
        Assert.Equal(today, model.SelectedDate);
        Assert.Equal(string.Empty, model.EntryText);
        Assert.True(model.Dictation.Command.CanExecute(null));
    }

    [Fact]
    public async Task SelectingTheShownDay_JustReturnsToTheEntryKeepingTheText()
    {
        await OpenList();
        model.EntryText = "keep typing";

        Row("2026-10-16").Select.Execute(null);

        Assert.True(model.IsEntryOpen);
        Assert.Equal("keep typing", model.EntryText);
    }

    [Fact]
    public async Task SelectingAPastRow_SavesUnsavedTextOfTodayFirst()
    {
        Past("2026-10-14", "Wednesday text");
        await model.Load();
        model.EntryText = "Unsaved Friday";
        model.ToggleListCommand.Execute(null);

        Row("2026-10-14").Select.Execute(null);

        connection.Verify(target => target.SaveEntry(today, "Unsaved Friday", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Wednesday text", model.EntryText);
    }

    [Fact]
    public async Task SelectingAPastRow_WhenTheSaveFails_StaysOnTodayInsteadOfLosingTheText()
    {
        Past("2026-10-14", "Wednesday text");
        await OpenList();
        model.EntryText = "precious";
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));

        Row("2026-10-14").Select.Execute(null);

        Assert.Equal(today, model.SelectedDate);
        Assert.Equal("precious", model.EntryText);
        Assert.True(model.IsDirty);
        Assert.Equal("offline", model.Error);
    }

    [Fact]
    public async Task SelectingAPastRow_WhenItsEntryCannotBeRead_StaysWhereItWasAndShowsTheError()
    {
        Past("2026-10-14", "Wednesday text");
        await OpenList();
        connection.Setup(target => target.GetEntry(Day("2026-10-14"), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));

        Row("2026-10-14").Select.Execute(null);

        Assert.Equal(today, model.SelectedDate);
        Assert.Equal("offline", model.Error);
        Assert.True(model.IsListOpen);
    }

    [Fact]
    public async Task Paging_LoadsFiftyDaysAtATimeAndTheNextPageOnDemand()
    {
        for (int index = 1; index <= 120; index++)
        {
            Past(new DateOnly(2026, 10, 15).AddDays(-index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), $"entry {index}", $"Title {index}", $"Summary {index}");
        }

        await OpenList();

        Assert.Equal(51, model.Rows.Count);
        Assert.True(model.HasMoreRows);
        connection.Verify(target => target.ListDigests(null, 50, It.IsAny<CancellationToken>()), Times.Once);

        await model.LoadMore();
        Assert.Equal(101, model.Rows.Count);
        Assert.True(model.HasMoreRows);

        await model.LoadMore();
        Assert.Equal(121, model.Rows.Count);
        Assert.False(model.HasMoreRows);
        Assert.Equal(model.Rows.Count, model.Rows.Select(row => row.Date).Distinct().Count());
        Assert.Equal(model.Rows.OrderByDescending(row => row.Date).Select(row => row.Date), model.Rows.Select(row => row.Date));

        connection.Invocations.Clear();
        await model.LoadMore();
        connection.Verify(target => target.ListDigests(It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Paging_AFewDaysNeedNoMorePages()
    {
        Past("2026-10-15", "Thursday");

        await OpenList();

        Assert.False(model.HasMoreRows);
    }

    [Fact]
    public async Task Paging_APageThatArrivesAfterTheListWasReopenedIsDropped()
    {
        for (int index = 1; index <= 80; index++)
        {
            Past(new DateOnly(2026, 10, 15).AddDays(-index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), $"entry {index}");
        }

        await OpenList();
        TaskCompletionSource<IReadOnlyList<EntryDigest>> slow = new();
        connection.Setup(target => target.ListDigests(It.Is<DateOnly?>(before => before != null), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(slow.Task);
        Task loading = model.LoadMore();
        model.ToggleListCommand.Execute(null);
        model.ToggleListCommand.Execute(null);

        slow.SetResult([new EntryDigest { Date = Day("2026-08-01"), Excerpt = "stale" }]);
        await loading;

        Assert.DoesNotContain(model.Rows, row => row.Date == Day("2026-08-01"));
        Assert.Equal(model.Rows.Count, model.Rows.Select(row => row.Date).Distinct().Count());
    }

    [Fact]
    public async Task Event_TitleArrivesForALoadedDay_UpdatesItsRowInPlace()
    {
        Past("2026-10-15", "Cooked pasta");
        await OpenList();
        EntryRowViewModel row = Row("2026-10-15");

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = Day("2026-10-15"), Digest = new EntryDigest { Date = Day("2026-10-15"), Title = "Pasta night", Summary = "You cooked for friends.", Excerpt = "Cooked pasta" } });

        Assert.Same(row, Row("2026-10-15"));
        Assert.Equal("Pasta night", row.Heading);
        Assert.Equal("You cooked for friends.", row.Body);
    }

    [Fact]
    public async Task Event_ANewEntryIsInsertedInDateOrder()
    {
        Past("2026-10-12", "Monday");
        Past("2026-10-15", "Thursday");
        await OpenList();

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = Day("2026-10-14"), Digest = new EntryDigest { Date = Day("2026-10-14"), Excerpt = "Wednesday from another phone" } });

        Assert.Equal([Day("2026-10-16"), Day("2026-10-15"), Day("2026-10-14"), Day("2026-10-12")], model.Rows.Select(row => row.Date));
    }

    [Fact]
    public async Task Event_ADeletedEntryLeavesTheListButTodaysRowStaysAsAnInvitation()
    {
        Past("2026-10-15", "Thursday");
        digests.Add(new EntryDigest { Date = today, Excerpt = "Today text" });
        await OpenList();

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = Day("2026-10-15") });
        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = today });

        EntryRowViewModel only = Assert.Single(model.Rows);
        Assert.True(only.IsToday);
        Assert.Equal("Tap to write today's entry", only.Body);
    }

    [Fact]
    public async Task Event_ADayOlderThanTheLoadedPagesIsLeftForALaterPage()
    {
        for (int index = 1; index <= 60; index++)
        {
            Past(new DateOnly(2026, 10, 15).AddDays(-index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), $"entry {index}");
        }

        await OpenList();
        int before = model.Rows.Count;

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = Day("2020-01-01"), Digest = new EntryDigest { Date = Day("2020-01-01"), Excerpt = "very old" } });

        Assert.Equal(before, model.Rows.Count);
    }

    [Fact]
    public async Task Event_WhileTheListIsClosed_LeavesTheRowsAlone()
    {
        Past("2026-10-15", "Thursday");
        await OpenList();
        model.ToggleListCommand.Execute(null);
        int rows = model.Rows.Count;

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = Day("2026-10-14"), Digest = new EntryDigest { Date = Day("2026-10-14"), Excerpt = "x" } });

        Assert.Equal(rows, model.Rows.Count);
    }

    [Fact]
    public async Task Event_EntryChangedOnTheShownDay_ReloadsTheTextUnlessTheUserIsTyping()
    {
        await model.Load();
        stored[today] = Entry(today, "From my other phone");

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = today, Digest = new EntryDigest { Date = today, Excerpt = "From my other phone" } });
        Assert.Equal("From my other phone", model.EntryText);

        model.EntryText = "typing now";
        stored[today] = Entry(today, "changed again");
        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = today });
        Assert.Equal("typing now", model.EntryText);
        Assert.True(model.IsDirty);
    }

    [Fact]
    public async Task Event_OtherEvents_AreIgnored()
    {
        await model.Load();
        connection.Invocations.Clear();

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.SummariesChanged });

        connection.Verify(target => target.GetEntry(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EditingText_MakesItDirtyAndSavable_ButBlankTextIsNotSavable()
    {
        await model.Load();

        model.EntryText = "Something";
        Assert.True(model.IsDirty);
        Assert.True(model.CanSave);

        model.EntryText = "   ";
        Assert.False(model.CanSave);
    }

    [Fact]
    public async Task Flush_StoresTheEntry()
    {
        await model.Load();
        model.EntryText = "A good day.";

        await model.Flush();

        connection.Verify(target => target.SaveEntry(today, "A good day.", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public async Task Flush_ServerRefuses_KeepsTheTextAndShowsTheError()
    {
        await model.Load();
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new RemoteException("The entry is empty."));
        model.EntryText = "text";

        await model.Flush();

        Assert.Equal("The entry is empty.", model.Error);
        Assert.True(model.IsDirty);
        Assert.Equal("text", model.EntryText);
    }

    [Fact]
    public async Task Flush_WithNothingChanged_SendsNothing()
    {
        await model.Load();

        await model.Flush();

        connection.Verify(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Flush_CalledTwiceAtOnce_SavesTheTextOnce()
    {
        await model.Load();
        model.EntryText = "once";

        await Task.WhenAll(model.Flush(), model.Flush());

        connection.Verify(target => target.SaveEntry(today, "once", It.IsAny<CancellationToken>()), Times.Once);
    }

    private async Task Until(Func<bool> done)
    {
        for (int attempt = 0; attempt < 100 && !done(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task AutoSave_SendsChangedTextEveryFiveSecondsAndNotBefore()
    {
        ManualTimeProvider time = new(DateTimeOffset.UnixEpoch);
        JournalViewModel saving = new(connection.Object, () => today, null, time);
        await saving.Load();
        saving.EntryText = "First words.";

        time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        connection.Verify(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        time.Advance(TimeSpan.FromSeconds(1));
        await Until(() => !saving.IsDirty);

        connection.Verify(target => target.SaveEntry(today, "First words.", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(saving.IsDirty);
    }

    [Fact]
    public async Task AutoSave_SavesAgainOnlyWhenThereIsNewText()
    {
        ManualTimeProvider time = new(DateTimeOffset.UnixEpoch);
        JournalViewModel saving = new(connection.Object, () => today, null, time);
        await saving.Load();
        saving.EntryText = "One.";

        time.Advance(TimeSpan.FromSeconds(5));
        await Until(() => !saving.IsDirty);
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        connection.Verify(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        saving.EntryText = "One. Two.";
        time.Advance(TimeSpan.FromSeconds(5));
        await Until(() => !saving.IsDirty);

        connection.Verify(target => target.SaveEntry(today, "One. Two.", It.IsAny<CancellationToken>()), Times.Once);
        connection.Verify(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AutoSave_NeverSendsBlankTextOrAnEarlierDaysEntry()
    {
        Past("2026-10-14", "Wednesday");
        ManualTimeProvider time = new(DateTimeOffset.UnixEpoch);
        JournalViewModel saving = new(connection.Object, () => today, null, time);
        await saving.Load();
        saving.EntryText = "   ";

        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        saving.ToggleListCommand.Execute(null);
        await Until(() => saving.Rows.Count > 1);
        saving.Rows.Single(row => row.Date == Day("2026-10-14")).Select.Execute(null);
        await Until(() => saving.IsReadOnly);
        saving.EntryText = "edited a read-only entry";
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        connection.Verify(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AutoSave_WhenTheServerCannotBeReached_KeepsTheTextAndTriesAgainNextTime()
    {
        ManualTimeProvider time = new(DateTimeOffset.UnixEpoch);
        JournalViewModel saving = new(connection.Object, () => today, null, time);
        await saving.Load();
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));
        saving.EntryText = "precious";

        time.Advance(TimeSpan.FromSeconds(5));
        await Until(() => saving.HasError);
        Assert.Equal("offline", saving.Error);
        Assert.True(saving.IsDirty);

        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, string text, CancellationToken _) => Entry(date, text));
        time.Advance(TimeSpan.FromSeconds(5));
        await Until(() => !saving.IsDirty);

        Assert.False(saving.IsDirty);
        Assert.Equal("precious", saving.EntryText);
    }

    [Fact]
    public async Task Detach_StopsTheAutoSave()
    {
        ManualTimeProvider time = new(DateTimeOffset.UnixEpoch);
        JournalViewModel saving = new(connection.Object, () => today, null, time);
        await saving.Load();
        Assert.Equal(1, time.ActiveTimers);
        saving.EntryText = "unsent";

        saving.Detach();
        time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.Equal(0, time.ActiveTimers);
        connection.Verify(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Load_CalledAgain_DoesNotStartASecondTimer()
    {
        ManualTimeProvider time = new(DateTimeOffset.UnixEpoch);
        JournalViewModel saving = new(connection.Object, () => today, null, time);

        await saving.Load();
        await saving.Load();

        Assert.Equal(1, time.ActiveTimers);
    }
}

public sealed class EntryRowViewModelTests
{
    private readonly DateOnly today = new(2026, 10, 16);

    private EntryRowViewModel Row(DateOnly date)
        => new(date, today, false, new AsyncCommand(_ => Task.CompletedTask));

    [Theory]
    [InlineData("2026-10-16", "Today")]
    [InlineData("2026-10-15", "Thu, Oct 15")]
    [InlineData("2026-01-02", "Fri, Jan 2")]
    [InlineData("2025-12-31", "Wed, Dec 31, 2025")]
    public void DateLabel_IsCompactAndShowsTheYearOnlyWhenItIsNotThisYears(string date, string label)
    {
        Assert.Equal(label, Row(DateOnly.Parse(date, CultureInfo.InvariantCulture)).DateLabel);
    }

    [Fact]
    public void Update_WithATitleShowsTitleAndSummary()
    {
        EntryRowViewModel row = Row(new DateOnly(2026, 10, 14));

        row.Update(new EntryDigest { Date = row.Date, Title = "A run", Summary = "You ran 5k.", Excerpt = "Ran 5k this morning" });

        Assert.Equal(("A run", "You ran 5k."), (row.Heading, row.Body));
        Assert.True(row.HasHeading);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("A run", null)]
    [InlineData(null, "You ran 5k.")]
    [InlineData("  ", "  ")]
    public void Update_WithoutBothTitleAndSummaryFallsBackToTheStartOfTheText(string? title, string? summary)
    {
        EntryRowViewModel row = Row(new DateOnly(2026, 10, 14));

        row.Update(new EntryDigest { Date = row.Date, Title = title, Summary = summary, Excerpt = "Ran 5k this morning" });

        Assert.False(row.HasHeading);
        Assert.Equal("Ran 5k this morning", row.Body);
    }

    [Fact]
    public void Update_Raises_PropertyChangedSoAnOpenListRefreshes()
    {
        EntryRowViewModel row = Row(new DateOnly(2026, 10, 14));
        List<string?> changed = [];
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        row.Update(new EntryDigest { Date = row.Date, Title = "A run", Summary = "You ran 5k.", Excerpt = "x" });

        Assert.Contains(nameof(EntryRowViewModel.Heading), changed);
        Assert.Contains(nameof(EntryRowViewModel.HasHeading), changed);
        Assert.Contains(nameof(EntryRowViewModel.Body), changed);
    }

    [Fact]
    public void Update_Null_ForAPastDayIsEmptyAndForTodayAnInvitation()
    {
        EntryRowViewModel past = Row(new DateOnly(2026, 10, 14));
        EntryRowViewModel now = Row(today);

        Assert.Equal(string.Empty, past.Body);
        Assert.Equal("Tap to write today's entry", now.Body);
    }
}
