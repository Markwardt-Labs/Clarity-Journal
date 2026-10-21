namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class JournalViewModelTests
{
    private readonly Mock<IServerConnection> connection = new();

    private readonly DateOnly today = new(2026, 10, 16);

    private readonly List<JournalEntry> stored = [];

    private readonly JournalViewModel model;

    public JournalViewModelTests()
    {
        connection.Setup(target => target.ListEntries(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly? from, DateOnly? to, CancellationToken _) => (IReadOnlyList<JournalEntry>)[.. stored.Where(entry => entry.Date >= from && entry.Date <= to)]);
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, string text, CancellationToken _) =>
        {
            JournalEntry entry = Entry(date, text);
            stored.RemoveAll(existing => existing.Date == date);
            stored.Add(entry);
            return entry;
        });
        model = new JournalViewModel(connection.Object, () => today);
    }

    private JournalEntry Entry(DateOnly date, string text) => new() { Date = date, Text = text, UpdatedAt = new DateTimeOffset(2026, 10, 16, 21, 5, 0, TimeSpan.Zero) };

    private CalendarDayViewModel Day(string date) => model.Days.Single(day => day.Date == DateOnly.Parse(date, CultureInfo.InvariantCulture));

    [Fact]
    public async Task Load_ShowsTodaysEditableEntryWithTheCalendarClosed()
    {
        stored.Add(Entry(today, "Today's words"));

        await model.Load();

        Assert.False(model.IsCalendarOpen);
        Assert.Equal(today, model.SelectedDate);
        Assert.Equal("Friday, October 16", model.SelectedTitle);
        Assert.Equal("Today's words", model.EntryText);
        Assert.True(model.CanEdit);
        Assert.False(model.IsReadOnly);
        Assert.True(model.HasEntry);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public async Task Load_NoEntryToday_ShowsAnEmptyEditor()
    {
        await model.Load();

        Assert.Equal(string.Empty, model.EntryText);
        Assert.False(model.HasEntry);
        Assert.False(model.CanSave);
        Assert.True(model.CanEdit);
    }

    [Fact]
    public async Task Calendar_IsAMondayFirstGridWithEntriesMarked()
    {
        stored.Add(Entry(new DateOnly(2026, 10, 14), "Wednesday"));
        stored.Add(Entry(today, "Today's words"));
        await model.Load();

        Assert.Equal("October 2026", model.MonthTitle);
        Assert.Equal(42, model.Days.Count);
        Assert.Equal(new DateOnly(2026, 9, 28), model.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 8), model.Days[41].Date);
        Assert.Equal([new DateOnly(2026, 10, 14), today], model.Days.Where(day => day.HasEntry).Select(day => day.Date));
        Assert.Equal(today, Assert.Single(model.Days, day => day.IsSelected).Date);
        Assert.Equal(today, Assert.Single(model.Days, day => day.IsToday).Date);
        Assert.False(model.Days[0].InMonth);
        Assert.True(model.Days[3].InMonth);
    }

    [Fact]
    public async Task ToggleCalendar_FlipsBetweenTheEntryAndTheCalendarNeverShowingBoth()
    {
        await model.Load();
        Assert.True(model.IsEntryOpen);
        Assert.False(model.IsCalendarOpen);

        model.ToggleCalendarCommand.Execute(null);
        Assert.True(model.IsCalendarOpen);
        Assert.False(model.IsEntryOpen);

        model.ToggleCalendarCommand.Execute(null);
        Assert.False(model.IsCalendarOpen);
        Assert.True(model.IsEntryOpen);
    }

    [Fact]
    public async Task SelectingTheShownDayInTheCalendar_FlipsBackToTheEntry()
    {
        await model.Load();
        model.EntryText = "keep typing";
        model.ToggleCalendarCommand.Execute(null);

        Day("2026-10-16").Select.Execute(null);

        Assert.True(model.IsEntryOpen);
        Assert.Equal("keep typing", model.EntryText);
    }

    [Fact]
    public async Task OnlyTodayAndEarlierDaysWithEntriesCanBeSelected()
    {
        stored.Add(Entry(new DateOnly(2026, 10, 14), "Wednesday"));
        stored.Add(Entry(new DateOnly(2026, 10, 20), "Tuesday next week"));
        await model.Load();

        Assert.True(Day("2026-10-16").Select.CanExecute(null));
        Assert.True(Day("2026-10-14").Select.CanExecute(null));
        Assert.False(Day("2026-10-15").Select.CanExecute(null));
        Assert.False(Day("2026-10-20").Select.CanExecute(null));
        Assert.False(Day("2026-10-17").Select.CanExecute(null));
        Assert.Equal([new DateOnly(2026, 10, 14), today], model.Days.Where(day => day.IsSelectable).Select(day => day.Date));
    }

    [Fact]
    public async Task SelectingAnEarlierDay_ShowsItsEntryReadOnlyAndClosesTheCalendar()
    {
        stored.Add(Entry(new DateOnly(2026, 10, 14), "Wednesday"));
        await model.Load();
        model.ToggleCalendarCommand.Execute(null);

        Day("2026-10-14").Select.Execute(null);

        Assert.False(model.IsCalendarOpen);
        Assert.Equal(new DateOnly(2026, 10, 14), model.SelectedDate);
        Assert.Equal("Wednesday, October 14", model.SelectedTitle);
        Assert.Equal("Wednesday", model.EntryText);
        Assert.True(model.IsEntryOpen);
        Assert.True(model.IsReadOnly);
        Assert.False(model.CanEdit);
        Assert.False(model.CanSave);
        Assert.False(model.Dictation.Command.CanExecute(null));
    }

    [Fact]
    public async Task SelectingTodayAgain_MakesTheEntryEditableAgain()
    {
        stored.Add(Entry(new DateOnly(2026, 10, 14), "Wednesday"));
        await model.Load();
        Day("2026-10-14").Select.Execute(null);
        model.ToggleCalendarCommand.Execute(null);

        Day("2026-10-16").Select.Execute(null);

        Assert.True(model.CanEdit);
        Assert.Equal(today, model.SelectedDate);
        Assert.Equal(string.Empty, model.EntryText);
    }

    [Fact]
    public async Task SelectingADayThatCannotBeSelected_DoesNothing()
    {
        await model.Load();
        model.ToggleCalendarCommand.Execute(null);

        Day("2026-10-15").Select.Execute(null);

        Assert.Equal(today, model.SelectedDate);
        Assert.True(model.IsCalendarOpen);
    }

    [Fact]
    public async Task SelectingAnEarlierDay_SavesUnsavedTextOfTodayFirst()
    {
        stored.Add(Entry(new DateOnly(2026, 10, 14), "Wednesday"));
        await model.Load();
        model.EntryText = "Unsaved Friday";

        Day("2026-10-14").Select.Execute(null);

        connection.Verify(target => target.SaveEntry(today, "Unsaved Friday", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Wednesday", model.EntryText);
    }

    [Fact]
    public async Task SelectingAnEarlierDay_WhenTheSaveFails_StaysOnTodayInsteadOfLosingTheText()
    {
        stored.Add(Entry(new DateOnly(2026, 10, 14), "Wednesday"));
        await model.Load();
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));
        model.EntryText = "precious";

        Day("2026-10-14").Select.Execute(null);

        Assert.Equal(today, model.SelectedDate);
        Assert.Equal("precious", model.EntryText);
        Assert.Equal("offline", model.Error);
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
    public async Task Flush_StoresTheEntryAndShowsWhenItWasSaved()
    {
        await model.Load();
        model.EntryText = "A good day.";

        await model.Flush();

        connection.Verify(target => target.SaveEntry(today, "A good day.", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(model.IsDirty);
        Assert.True(model.HasEntry);
        Assert.Contains(model.Days, day => day.Date == today && day.HasEntry);
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
    public async Task MonthButtons_MoveTheCalendarWithoutChangingTheSelectedDay()
    {
        await model.Load();
        model.ToggleCalendarCommand.Execute(null);

        model.NextMonthCommand.Execute(null);
        Assert.Equal("November 2026", model.MonthTitle);
        Assert.Equal(new DateOnly(2026, 10, 26), model.Days[0].Date);
        Assert.Equal(today, model.SelectedDate);

        model.PreviousMonthCommand.Execute(null);
        model.PreviousMonthCommand.Execute(null);
        Assert.Equal("September 2026", model.MonthTitle);
        connection.Verify(target => target.ListEntries(new DateOnly(2026, 10, 26), new DateOnly(2026, 12, 6), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReopeningTheCalendar_ShowsTheMonthOfTheShownDay()
    {
        await model.Load();
        model.ToggleCalendarCommand.Execute(null);
        model.NextMonthCommand.Execute(null);
        model.ToggleCalendarCommand.Execute(null);

        model.ToggleCalendarCommand.Execute(null);

        Assert.Equal("October 2026", model.MonthTitle);
    }

    [Fact]
    public async Task Handle_EntryChangedOnTheShownDay_ReloadsTheTextUnlessTheUserIsTyping()
    {
        await model.Load();
        stored.Add(Entry(today, "From my other phone"));

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = today });
        Assert.Equal("From my other phone", model.EntryText);

        model.EntryText = "typing now";
        stored[0] = Entry(today, "changed again");
        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = today });
        Assert.Equal("typing now", model.EntryText);
        Assert.True(model.IsDirty);
    }

    [Fact]
    public async Task Handle_OtherEvents_AreIgnored()
    {
        await model.Load();
        connection.Invocations.Clear();

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.SummariesChanged });

        connection.Verify(target => target.ListEntries(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Load_ConnectionFails_ShowsTheError()
    {
        connection.Setup(target => target.ListEntries(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("The connection to the server was lost."));

        await model.Load();

        Assert.Equal("The connection to the server was lost.", model.Error);
        Assert.Empty(model.Days);
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
        stored.Add(Entry(new DateOnly(2026, 10, 14), "Wednesday"));
        ManualTimeProvider time = new(DateTimeOffset.UnixEpoch);
        JournalViewModel saving = new(connection.Object, () => today, null, time);
        await saving.Load();
        saving.EntryText = "   ";

        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        saving.Days.Single(day => day.Date == new DateOnly(2026, 10, 14)).Select.Execute(null);
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
}
