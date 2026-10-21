# Architecture

This document explains the high-level design decisions behind Clarity Journal: why it is built the way it is, not the class-by-class mechanics.

## Core, Server, Frontend and two heads

`Core` holds the wire protocol and models both sides must agree on, so neither side references the other. The alternative, the app depending on the server project, was rejected because it would drag the SQLite and process code into the Android build. The trade-off is that shared code must stay platform neutral. `Client/Frontend` is a plain `net10.0` Avalonia library with every view, view model and service; `Client/Mobile` (Android) and `Client/Desktop` are thin heads that only host its root view. Keeping the shared code out of the Android assembly is also what lets the headless UI tests load it.

## The desktop head exists for development only

The product is the Android app. The desktop head hosts the same view in a phone sized window so the UI can be run, exercised and screenshotted on a development machine without an emulator. It is never published and holds no behavior of its own, which is what keeps it a faithful preview.

## The server owns the data and Claude Code

Entries live in a SQLite database on the user's own computer, and Claude Code runs there too, so the journal never leaves the user's machines and the app needs no account or cloud service. The alternative, summarizing on the phone, was rejected because Claude Code is a local command on a computer, not something a phone can run, and because a phone is often asleep or offline when a weekly job is due. The trade-off is that the server must be reachable and running to use the app.

## Days are keyed by the date the client chooses

The primary key of an entry is its calendar date, which makes "exactly one entry per day" a database constraint instead of a convention, and saving a replace instead of a create-or-update decision. The phone chooses the date, not the server, so an entry belongs to the day the person lived even when the server is in another time zone.

## Summaries are incremental, driven by a revision counter

Every save, including an edit, gets the next number of a global revision counter. A summary records the revision through which it has taken in every entry of its period, so an update reads only entries with a higher revision and passes Claude Code the previous summary plus those entries, never the whole journal. A timestamp watermark was rejected because clocks move and edits of old days would look old; a counter in the same transaction as the write cannot be out of order. The trade-off, stated in the component document, is that a deleted entry cannot be removed from a summary that already took it in.

## Claude Code runs as one process per request

Each summary update or chat answer starts `claude -p` once and waits for its JSON result. A long-lived streaming process, as an interactive agent client would use, was rejected because nothing here needs mid-run interaction, and a process per request has no state to recover when it dies. Chat continuity comes from Claude Code's own session id, which the server stores, not from a process that stays alive.

## Chat reads files, not the database

Claude Code's safe, read-only tools work on files. The server therefore mirrors the journal as markdown files (one per entry and per summary) and runs chat answers with only the Read, Grep and Glob tools, pre-approved, in that folder, with every other permission denied. The alternative, giving Claude Code a database tool or a shell, was rejected because it widens what a prompt could reach on the user's machine. The cost is a second copy of the text, rebuilt from the database at every start so it cannot drift.

## Secure WebSocket with a trusted-on-first-use certificate

The server speaks `wss` with a self-signed certificate it generates once, on a single port, authenticating the app with an access key after the TLS handshake. The app pins the certificate fingerprint it saw on the first connection and refuses a different one later, because a self-signed certificate cannot be checked against a certificate authority and accepting any certificate every time would let a person on the network read the key and the journal. The alternative, a certificate authority issued certificate, needs a domain name the typical home server does not have; `certificateFile` supports it for those who do.

## Summaries run only on the server's schedule

The server runs the update every Friday at a configured local time, and when it starts and finds the last run older than the most recent Friday time, it runs at once, so a server that is off on Friday evening still catches up. A run that finds no entry saved since the last one asks Claude Code nothing. There is deliberately no way to start an update from the app: a person writing a journal has no reason to run a model on demand, and the week's summary is more useful written once the week has ended.

## Chat is short lived

The chat is stored on the server so every device sees it and an answer is never lost to a dropped connection, but only the last two days of it are kept: a check at startup and after every local midnight deletes older messages. The journal and its summaries are the lasting record Claude Code answers from, so an old conversation adds nothing but stale context. When the deletion empties the chat, the Claude Code conversation behind it is forgotten too, so the next question starts fresh.

## Voice input is turned into text on the server

The phone records speech and sends it to the server, which transcribes it with a local Whisper model (or any command the user configures). The alternative, Android's built-in speech recognition, was rejected because it needs the platform's recognition service, which is not available on every phone and may send audio to a cloud service, while this app promises that the journal stays on the user's own machines. The trade-off is a one-time model download on the server (about 470 MB for the default) and a few seconds of waiting after speaking.
