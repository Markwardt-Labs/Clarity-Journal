# Updates

The app and the server update themselves from the newest GitHub release of `Markwardt-Labs/Clarity-Journal`. The development preview head only updates the server it is connected to, since it is not a released program.

## Finding the release

`IReleaseFeed` reads the latest release from the public GitHub API without a token, which leaves out drafts and pre-releases. The tag is the version, so a tag that is not `major.minor.patch` with an optional suffix, such as a stray `1.p`, is ignored and counts as no release. Versions are ordered by `AppVersion`: numbers first, and a suffixed version is earlier than the final release. Downloads are checked against the size and the SHA-256 digest GitHub states for each asset, and a mismatch discards the file, so a truncated or tampered download is never installed. GitHub allows 60 anonymous requests an hour per address, and the app looks up once an hour plus once per reconnect, which leaves plenty.

The assets this relies on are named exactly `ClarityJournal-Server-linux`, `ClarityJournal-Server-win.exe` (both x64) and `ClarityJournal.apk`; the release workflow produces them, and renaming one breaks updating from the app.

## Who knows which version

The server reports its version in the handshake, and the app reads it again whenever a connection is reopened, because a server that updated while the app was connected comes back as a different version on the same connection object. The app compares three versions with the newest release: its own, the server's, and a server that reports none (one older than this system) counts as older. An update is offered when the release is newer than the app, if the app can install itself, or newer than the server.

There is deliberately no screen that blocks the app when the two versions differ. The journal stays usable; a pair that is really incompatible shows the server's error messages, and Options keeps offering the update until both are current.

## The app

`UpdateViewModel` looks up the newest release when the app connects, after every reconnect (which includes coming back from the background) and every hour, and a failed lookup, such as without internet, is silent and tried again at the next interval. Options shows an "Update to x.y.z" option only while an update is available. Choosing it opens a confirmation that says what is replaced and, when the server is updated, that its restart interrupts a summary or answer it is writing. Confirming runs, in order:

1. Today's entry is sent to the server first, because both the restart and the install end work in progress. If it cannot be saved, nothing is updated and the reason is shown.
2. The server is told to update, which comes first so that an app that restarts has already told its server.
3. The app downloads its package and hands it to the system installer.

From the confirmation on, the updating screen replaces the whole app (no tab bar) and shows what is happening and the download progress; it cannot be left while the work runs. A failure goes back to the confirmation with the reason, and the update can be tried again. After a server-only update the screen waits for the server to come back and then returns by itself to where the user was. After the package was handed to the installer the screen stays until closed, because the system finishes the update.

An app that restarts after its update usually starts before its server is back, so a saved server that cannot be reached is tried again every few seconds for two minutes while the connect screen shows the error, and the session opens by itself when the server answers. The retrying stops if the key is rejected, if the certificate changed, or when the user touches the connect screen.

### Android

`AndroidUpdater` downloads the package into the app's cache and hands it to the system installer through a `FileProvider`. The first time, the user has to allow Clarity Journal to install apps, which the app opens the settings for. The installer only accepts a package signed with the key of the installed app, which the release builds are, so a debug build cannot be updated this way.

The installer also compares version codes, and the build used to give every release the code 1. The code is now derived from the release version, `major * 1000000 + minor * 1000 + patch`, so every release is higher than the one before; a build without a version keeps the default of 1.

## The server

The app sends `UpdateServer`. The server compares its own version with the newest release, downloads the build for its platform next to the running executable (so the later move never crosses a file system), and asks `IServerRestart` to shut it down after a second, so the answer reaches the app first. Failures (GitHub unreachable, no newer release, no build for this platform, an unwritable folder, a checksum mismatch) come back as a message the app shows, and the server keeps running. Only one update runs at a time.

Once the server's sessions and listener are closed it puts the downloaded file in place of the running one. On Linux that is a rename over the running file. Windows refuses that but allows renaming a running executable, so the old one becomes `.old` and is deleted at the next start, and is put back if the new file cannot be moved. A server started through `dotnet` has no executable of its own to replace, so it refuses to update. A single-file program loads its assemblies lazily from its executable on disk, so before the swap the server starts a harmless process to get everything loaded that starting one needs, and afterwards it only starts the new executable and exits.

Where the new version comes back depends on who started the server:

- Under a service manager (systemd sets `INVOCATION_ID`) the server exits with code 75 and relies on the manager restarting it, because the manager would stop a process the server started itself. The unit needs `Restart=on-failure` or `Restart=always`; without a restart policy the server stays down after an update.
- Otherwise the server starts the new executable with the same command line.

The restart ends a chat answer or a summary run in progress, like any restart; the question stays in the chat unanswered, and the summary picks up at the next scheduled run.

## The first update

The mechanism has to exist on both sides before it can be used, so the first release that contains it cannot be installed from the app: an older server does not know the command and an older app has no screen for it. That release is installed by hand once, the server and then the APK; every release after it updates from the app.
