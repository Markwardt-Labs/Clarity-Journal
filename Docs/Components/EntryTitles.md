# Entry titles

`EntryTitler`, `EntryTitleScheduler`, `EntryDigests` and the title parts of `Prompts` and `EntryStore` give every finished day a short title and a one or two sentence summary, and `JournalService` carries them to the app.

## What counts as pending

An entry is pending when its date is before the server's today and its title or summary is missing. Today is excluded on purpose: it is still being written. An edit of any entry clears both fields, so an edited past day becomes pending again, and a save of unchanged text leaves them alone. Pending entries are taken newest first, so the days a reader is most likely to open get their titles first when many are missing.

## One run

For each pending entry the titler asks Claude Code once, with no tools, a system prompt that replaces the coding prompt, and a prompt that names the date and weekday and gives the whole entry. The system prompt asks for a bare JSON object with `title` and `summary`: a title of two to six words that says what the day was about, and one or two short sentences to the person in the second person. One entry per call means a bad answer costs one entry, and a long journal does not need one huge prompt.

The answer is read leniently, because models wrap JSON in code fences or add a sentence around it: everything from the first `{` to the last `}` is parsed. Whitespace is collapsed, surrounding quotation marks and a final period on the title are removed, a title over 80 characters and a summary over 300 are cut with `...`, and an answer without both fields, with a field that is not a string, or with an empty one is not stored. An unreadable answer skips that entry and the run goes on; Claude Code failing (not signed in, a timeout) or a storage error ends the run, and what is still missing is picked up next time.

## Storing safely

The titler stores the result only if the entry still has the text it was made from. Generation takes seconds, and a title describing words that have since been edited away would be worse than none. A stored title is announced with an `EntryChanged` event carrying the new digest, so an open list updates that row in place. Storing a title is not a save: it takes no revision and so does not make the summaries read the entry again.

## When it runs

`EntryTitleScheduler` runs the titler as soon as the server starts, which is the catch-up for a day change that happened while the server was off, and then again after every local midnight. It wakes five seconds after midnight, since a timer that fires a few milliseconds early would still see the old day and would then wait a whole day to see the one that just ended. After a failed run it tries again an hour later, or at midnight if that comes sooner. The generation runs in the background beside the summaries and the chat, which use Claude Code independently.

## What the list shows

`EntryDigest` holds the date, the title and summary, and an excerpt, the start of the text on one line (whitespace collapsed, 160 characters). The list uses the excerpt for a day without a title, so a row is never empty. The excerpt is built from the first 400 characters of the stored text, so a page of days never reads whole entries.
