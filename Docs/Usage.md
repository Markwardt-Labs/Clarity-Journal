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

Run it as the user that is signed in to Claude Code, because the server starts `claude` as that user, and set `claudeCommand` to the absolute path of `claude`, since a service does not have your shell's `PATH`.

A systemd user service needs no root and does the same: put the unit in `~/.config/systemd/user/clarity-journal.service` with `ExecStart=%h/.local/opt/clarity-journal/Markwardt.ClarityJournal.Server %h/.config/clarity-journal/config.json` and an `Environment=PATH=%h/.local/bin:/usr/local/bin:/usr/bin:/bin` line, then run `systemctl --user enable --now clarity-journal` and, so it also runs when nobody is logged in, `loginctl enable-linger $USER`. Read its output with `journalctl --user -u clarity-journal`. To update, replace the executable and run `systemctl --user restart clarity-journal`.

## Connecting the app

Enter the server's address, the port and the key. When the server presents its own self-signed certificate, the first connection trusts it and remembers its fingerprint; compare it with the one the server printed if you want to be certain. A later connection that presents a different certificate is refused, and the app offers "Trust new certificate" for when you replaced it on purpose. A certificate that a public authority vouches for, as with a hostname behind Cloudflare below, is trusted like any website's and is not pinned, because such certificates are renewed regularly.

## Reaching the server from outside your network

The app needs a route to the server's port. On a home network that is the computer's local address. Away from home there are two good ways that need no port forwarding on the router:

- A private network such as a VPN or a mesh network (Tailscale, WireGuard), where the phone uses the server's private address and the key and certificate pinning protect the connection.
- A Cloudflare Tunnel, which gives the server a public hostname with a real certificate while the connection to Cloudflare is made outward from the server. Add the hostname to the tunnel's ingress, pointing at the server's port with `noTLSVerify: true` because the server's own certificate is self-signed, and create the DNS record:

```yaml
ingress:
  - hostname: clarity.example.com
    service: https://localhost:52681
    originRequest:
      noTLSVerify: true
  - service: http_status:404
```

```bash
cloudflared tunnel route dns <tunnel name> clarity.example.com
```

In the app the host is then `clarity.example.com` and the port is `443`. Cloudflare closes a WebSocket that stays silent for 100 seconds, which the app avoids by sending its own keep-alive every 20 seconds. The hostname is public, so the access key is the only thing between the internet and the journal: use a long random one.

## Running everything from source

`Scripts/RunDesktop.task` lists the dev server and the desktop preview window, which a task runner starts together; the same two scripts also run by hand in two terminals:

```bash
dotnet run Scripts/Support/DevServer.cs
dotnet run Scripts/Support/DesktopClient.cs
```

The dev server uses the key `dev` and keeps its data under `Scripts/Support/Data/`. `Scripts/RunMobile.task` pairs the same server with an Android emulator, which reaches the host at `10.0.2.2`.
