# Usage

Runnable examples for the server and the app.

## Minimum server setup

```json
{ "key": "choose-a-long-secret" }
```

Save it as `config.json` next to the server executable and run `./ClarityJournal-Server-linux` (or pass another path as the first argument). The server prints the port it listens on and its certificate's SHA-256 fingerprint. Everything it stores, including the generated certificate, goes to the `ClarityJournal` folder of the server user's application data.

## Full server config

```json
{
  "port": 52681,
  "key": "choose-a-long-secret",
  "dataFolder": "/srv/clarity-journal",
  "claudeCommand": "claude",
  "claudeArguments": ["--model", "sonnet"],
  "claudeTimeoutMinutes": 10,
  "summaryTime": "20:00:00",
  "summaryBatchCharacters": 40000,
  "chatRetentionDays": 2,
  "speechModel": "small.en",
  "certificateFile": "/etc/clarity/server.pfx",
  "certificatePassword": "optional"
}
```

`summaryTime` is the server's local time on Fridays. `claudeArguments` are added to every Claude Code run, which is the place to pick a model. `chatRetentionDays` is how many days of chat are kept. Without `certificateFile` the server creates a self-signed certificate once and keeps it.

## Voice input

The app's microphone buttons record speech and the server turns it into text, by default with a Whisper model it runs itself. Nothing needs installing: the server downloads the model named by `speechModel` into the `models` folder of its data folder when it first starts (about 470 MB for `small.en`; use `tiny.en` or `base.en` for a smaller one), and a recording made before the download ends waits for it. It uses the GPU through Vulkan when the machine has a working driver and the CPU otherwise. `.en` models are faster and more accurate for English; models without it detect the language. Set `speechModel` to an empty string to turn voice input off, or set `speechCommand` and `speechArguments` (with `{file}` standing for the recorded WAV file) to use another program that prints the text:

```json
{ "key": "choose-a-long-secret", "speechModel": "", "speechCommand": "whisper-cli", "speechArguments": ["-m", "/models/ggml-medium.bin", "-nt", "-f", "{file}"] }
```

The Android app asks for the microphone permission the first time a microphone button is used.

## Keeping the server running on Linux

```ini
[Unit]
Description=Clarity Journal server
After=network-online.target

[Service]
User=you
WorkingDirectory=/home/you/clarity-journal
ExecStart=/home/you/clarity-journal/ClarityJournal-Server-linux /home/you/clarity-journal/config.json
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Run it as the user that is signed in to Claude Code, because the server starts `claude` as that user.

## Connecting the app

Enter the server's address, the port and the key. The first connection trusts whatever certificate the server presents and remembers its fingerprint; compare it with the one the server printed if you want to be certain. A later connection that presents a different certificate is refused, and the app offers "Trust new certificate" for when you replaced it on purpose.

## Reaching the server from outside your network

The app needs a route to the server's port. On a home network that is the computer's local address. Away from home, put the server and the phone on a private network such as a VPN or a mesh network (Tailscale, WireGuard) instead of exposing the port to the internet; the key and certificate pinning protect the connection, but a journal is worth the extra layer.

## Running everything from source

`Scripts/RunDesktop.task` lists the dev server and the desktop preview window, which a task runner starts together; the same two scripts also run by hand in two terminals:

```bash
dotnet run Scripts/Support/DevServer.cs
dotnet run Scripts/Support/DesktopClient.cs
```

The dev server uses the key `dev` and keeps its data under `Scripts/Support/Data/`. `Scripts/RunMobile.task` pairs the same server with an Android emulator, which reaches the host at `10.0.2.2`.
