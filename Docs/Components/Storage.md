# Storage

`Database`, `MetaStore`, `EntryStore`, `SummaryStore` and `ChatStore` keep everything in one SQLite file, `journal.db`, in the data folder.

## Tables

`entries` is keyed by the date as `yyyy-MM-dd` text, so the one-entry-per-day rule is the primary key and ISO dates sort and compare correctly as text. `summaries` is keyed by kind and period identifier and holds the progress value next to the text. `chat_messages` has an increasing id that doubles as the order. `meta` holds small named values: the revision counter, the summarizing progress, the last run time and the chat session id.

## Connections

Each store call opens its own connection and the driver pools them, so there is no connection shared between the request threads, the scheduler and the background chat answers. The database runs in write-ahead logging mode so a long read does not block a save.

## Revisions

The save of an entry increments the counter in `meta` and writes the entry with the new value in one transaction, so concurrent saves each get a distinct, increasing revision and a failed save leaves no gap-induced confusion. The counter lives in its own row rather than being derived from the entries because deriving it from the highest stored revision would reuse a number after the newest entry is deleted, and a summary already past that number would then skip the next save.

## Time

Instants are stored as round-trip ISO 8601 text with their offset and read back as the same instant. Dates have no time of day and no offset.
