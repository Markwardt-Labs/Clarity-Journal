# Clarity Journal

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Build](https://github.com/Markwardt-Labs/Clarity-Journal/actions/workflows/build.yml/badge.svg)](https://github.com/Markwardt-Labs/Clarity-Journal/actions/workflows/build.yml)
![Coverage](.github/badges/badge_linecoverage.svg)

Clarity Journal is a private journalling app. An Android app writes one journal entry per day to a command line server on your own computer, which keeps every entry in a SQLite database. Every Friday evening the server asks your local Claude Code to write week, month, year and lifetime summaries, building each one on the previous summary and only the entries added since, and the app shows the summaries and a chat in which you can ask Claude Code questions about your entries. The phone reminds you at noon and at 8 pm every day to write. Entries and questions can be spoken: the phone records, and the server turns the speech into text on your own machine. Built on .NET 10 and [Avalonia](https://avaloniaui.net/); see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for redistributed dependency licenses.

## Requirements

- Server: Windows 10/11, 64-bit, or Linux, x86-64 (glibc-based distro). No separate .NET runtime install needed: the server executable is self-contained.
- The `claude` command (Claude Code), installed and signed in on the server's machine. Without it entries are still saved, but summaries and chat answers fail with a message in the app.
- About 470 MB of disk space on the server for the speech model, which it downloads when it first starts (a smaller model or none can be chosen in the config).
- Phone: Android 7.0 (API 24) or newer, able to reach the server over the network.

## Installing

Download the files for your devices from the latest [Release](https://github.com/Markwardt-Labs/Clarity-Journal/releases):

- **Server, Windows** - download `ClarityJournal-Server-win.exe` and run it.
- **Server, Linux** - download `ClarityJournal-Server-linux`, mark it executable (`chmod +x ClarityJournal-Server-linux`), and run it.
- **Phone** - download `ClarityJournal.apk` and install it.

## Getting started

Create a `config.json` next to the server executable:

```json
{ "key": "choose-a-long-secret" }
```

Start the server, which prints its port and certificate fingerprint, then open the app, enter the server's address, the port (52681 unless changed) and the key, and tap Connect.

## Documentation

| File | Covers |
| --- | --- |
| [Docs/Api.md](Docs/Api.md) | Shape and flow of the shared `Core` protocol |
| [Docs/Usage.md](Docs/Usage.md) | Running the server and connecting the app |
| [Docs/Architecture.md](Docs/Architecture.md) | High-level design decisions |
| [Docs/Project.md](Docs/Project.md) | Repository tooling: scripts, publishing, CI |
| [Docs/Components/](Docs/Components) | One file per complex internal component |
