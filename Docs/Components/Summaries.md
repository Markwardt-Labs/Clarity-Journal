# Summaries

`Summarizer`, `SummarySchedule`, `SummaryScheduler`, `Prompts` and `SummaryPeriod` turn the entries into week, month, year and lifetime summaries with Claude Code, reading only what is new. Nothing but the scheduler starts a run; the app can only read the results.

## What counts as new

Every save of an entry, including an edit of an old day, takes the next number of a global revision counter kept in the same transaction as the write. A stored summary records the highest revision it has taken in (`ThroughRevision`), and the store records one global value, the revision through which every summary is complete. An update lists the entries after that global value up to the revision that is latest when the update starts, so an entry saved mid-run waits for the next run instead of being half counted. When that list is empty the run asks Claude Code nothing, changes no summary and only records that it ran.

## One run

For each kind in turn (week, month, year, lifetime), the new entries are grouped by the period `SummaryKind.PeriodOf` puts their date in. Each group keeps only the entries whose revision is above its own summary's `ThroughRevision`: this is what makes a run that failed halfway cheap to repeat, since the periods already finished are skipped. A period then asks Claude Code to update its summary from the previous summary text and those entries.

A first run over a long journal produces one call per period that has entries, each small. A week that gets a late Saturday entry is updated by the next Friday run, because the entry's date, not the run's date, picks the period.

## Batching

An update never puts more than `summaryBatchCharacters` of entry text in one prompt. Longer groups are split into batches in revision order and folded one after another, each batch starting from the summary the previous one produced and saved immediately with the revision of its last entry. Revision order, not date order, is what keeps `ThroughRevision` meaningful when an old day is edited. Within a prompt the entries are listed by date.

## Lifetime

The lifetime is one fixed period, so every entry belongs to it. Its first and last day are the earliest and latest entry days it has seen, since it has no calendar bounds.

## The prompt

The system prompt replaces Claude Code's coding prompt and asks for the whole updated summary as markdown, in second person, with a length per kind. The request prompt names the period, gives the previous summary (or says there is none), and lists the new entries each marked with its date and weekday, noting that an entry replaces any earlier version of the same day. Claude Code runs with no tools and no stored session.

## What a summary cannot do

A deleted entry stays in any summary that already took it in, and an edit is only a replacement the model is told about. Rebuilding from scratch means deleting the summaries and resetting the stored progress.

## Schedule and catch-up

`SummarySchedule` is pure date arithmetic: the next Friday at the configured time after a moment, and the latest one at or before it. `SummaryScheduler` loops: if no run is recorded, or the recorded run is older than the latest scheduled time, it runs at once; otherwise it waits for the next scheduled time. So starting the server on any day checks whether the last Friday's update happened, using the server's own clock, and does it then if not; starting it on a Friday morning waits for that evening. A run with nothing new still records that it ran, which is what stops the next start from checking again. A failed run is retried after an hour.

## Failures

A Claude Code failure, a storage failure or a file error ends the run, publishes a `Failed` event and leaves the progress values untouched, so the retry continues where it stopped.
