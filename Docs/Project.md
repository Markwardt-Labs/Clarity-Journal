# Project

Documentation for this repository's own tooling and workflow.

## Scripts

`Scripts/` holds file-based C# apps (`dotnet run Scripts/<Name>.cs`, from the root) and tasks for automated repository actions. Only what a developer runs by hand is in the folder itself; what those are built from is in `Scripts/Support/`.

- `Scripts/Publish.cs` - cuts a release (see Publishing below). A manual, human-only action.
- `Scripts/RunDesktop.task` - runs the dev server and the desktop preview client together. A `.task` file lists scripts, one per line, and the scripts of a task run together.
- `Scripts/RunMobile.task` - runs the dev server and deploys the Android app to an emulator.
- `Scripts/Support/` - the scripts the tasks above are built from, which are not run directly, and `Data/`, the git-ignored state they keep.

## Publishing

Run `Scripts/Publish.cs` locally to cut a release:

1. It prompts for the version to publish (e.g. `1.2.3`); no project carries a `<Version>` of its own, so this is what actually gets built and published.
2. It creates the GitHub Release (and its underlying tag) for that version locally via `gh release create`, done locally because a repo ruleset blocks the default `GITHUB_TOKEN` from creating tags.
3. It dispatches `build.yml`'s `workflow_dispatch` trigger with the version as input. The workflow verifies the dispatcher has Admin permission on the repo, refuses to run from anything but `main`, publishes the self-contained single-file server for Windows and Linux (x64) and the signed Android APK, and uploads them alongside `THIRD-PARTY-NOTICES.txt` as release assets.
4. It waits for that run to finish, rolling the release/tag back if the workflow fails, so a failed publish never leaves one behind. If it can't confirm the run happened at all, it leaves the release in place instead, rather than risk deleting one that's still running.

The APK is signed with a release keystore kept in repository secrets: `ANDROID_KEYSTORE_BASE64` (the keystore, base64 encoded), `ANDROID_KEYSTORE_PASSWORD` (one password for the store and the key) and the key alias `clarityjournal`.

The workflow does not trust the signing settings alone, because without them the Android build quietly signs with a debug key. After publishing it checks the finished `com.markwardt.clarityjournal-Signed.apk` with `apksigner`: the file must exist, must not carry the Android debug certificate and must be signed by exactly the release keystore (the certificate's SHA-256 is compared with the keystore's), or the run fails before anything is uploaded. After the upload it also confirms that the release lists `ClarityJournal.apk` among its assets. The APK is attached under that name, next to the two server executables and `THIRD-PARTY-NOTICES.txt`.

## Workflows

- `.github/workflows/build.yml` - builds with the Android workload, verifies formatting (`dotnet format --verify-no-changes`, never applies fixes), and runs tests on every push/PR to `main`; its `publish` job (see Publishing above) only runs on `workflow_dispatch`, gated on the dispatcher having Admin permission on the repo and the run being on `main`.
