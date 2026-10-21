# API

The shared surface is the wire protocol in `Core`: a handshake followed by requests, responses and server pushed events, all as one JSON line each over a secure WebSocket. This document covers the design and flow of that protocol, not the member-level detail already in the source.

## Shape

One request type, `RemoteRequest`, carries a `RemoteCommand` and the few optional fields any command needs (a date, a range, a kind, a text). One response type, `RemoteResponse`, has one optional field per kind of answer plus an `Error`. The alternative, a request and response class per command, was rejected because the command set is small and flat, so a single pair keeps the client's connection class a list of one-line wrappers and keeps the JSON trivial to read in a log. The trade-off is that a field only means something for some commands, which each command's documentation states.

Models are plain records: `JournalEntry` (one per day, keyed by `Date`), `Summary` (a kind and a period identifier), `ChatMessage` and `ChatHistory`. `SummaryKind.PeriodOf` is the one place that knows how a day maps to its week (Monday to Sunday), month, year or the single lifetime period, so the server groups entries and the app labels them from the same rule.

## Flow

1. The client opens a secure WebSocket to the server's port and sends a `HandshakeRequest` with the access key as the first line.
2. The server answers with a `HandshakeResponse`. A wrong key or a malformed line gets `accepted: false` and the connection is closed; only a client that sends nothing within five seconds is closed without a reply.
3. The client sends `RemoteRequest` lines. Each carries a client-chosen `Id` and may overlap with others; the matching `RemoteResponse` echoes the `Id`, and a failure is an `Error` string, never a closed connection.
4. The server pushes `RemoteResponse` lines with `Id` zero and an `Event` to every connected client: an entry changed, summaries changed, a chat message arrived, Claude Code started or stopped answering, the chat was cleared or trimmed, or a run failed.

## Slow work returns at once

`SendChat` does not wait for Claude Code, which can take minutes. It returns as soon as the question is stored, and the answer arrives as events (`ChatMessageAdded`, `ChatWorkingChanged`). A client that disconnects meanwhile loses nothing: the answer is stored, and `GetChat` returns it after reconnecting. Every client, including the sender, learns of a new chat message from the event, so a second phone shows the same conversation.

## Summaries have no command to run them

Clients can only read summaries (`GetSummaries`). The server writes them by itself every Friday evening and when it starts after a missed Friday, and announces each written summary with `SummariesChanged`; a failed run is announced with `Failed`.

## Voice input

`Transcribe` takes a base64 WAV recording (16 kHz, mono, 16 bit, up to 4 MB) and answers the text the server's speech model or command made of it, empty when nothing was heard. It is not tied to a journal day or to the chat, so any text box can use it, and it fails with a message when voice input is turned off on the server.

## Journal rules the protocol keeps

- A day has at most one entry. `SaveEntry` replaces the entry of its `Date`; there is no create versus update.
- The client chooses the date, so an entry belongs to the day the person lived it in, whatever the server's time zone is. The server accepts dates up to one day ahead of its own today to allow for that.
- `GetEntry` of a day without an entry is not an error: the response simply has no `Entry`.
- Blank entries are refused; deleting is a separate command.
