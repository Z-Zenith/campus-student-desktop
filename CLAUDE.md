# campus-student-desktop

Student Desktop App (SDA) — the locked-down student desktop client, part of the Campus
Digitalization Platform. Split out of the `Omega` monorepo; see
[campus-platform/docs/Campus platform architecture.md](https://github.com/Z-Zenith/campus-platform/blob/main/docs/Campus%20platform%20architecture.md)
for the full system architecture and `campus-platform/INTEGRATIONS.md` for which tagged
versions of this repo are compatible with which backend/shared-lib versions.

This repo's history was extracted via `git subtree split`, so `git log`/`git blame` here are
scoped to `apps/student-desktop/` from the original monorepo — commits that didn't touch this
path don't appear. See "About this repo's history" below.

## Tech stack

Avalonia (.NET/C#), .NET 10 — chosen over MAUI (no official Linux support). MVVM via
CommunityToolkit.Mvvm. Whitelisted in-app browser and embedded editor surfaces use
`Avalonia.Controls.WebView` (WebView2/WKWebView/WPE WebKit).

## Build & test

```bash
dotnet build
dotnet run
dotnet test   # StudentDesktop.Tests
```

## Cross-repo NativeWebView integrations (SDA-19, SDA-24)

Two features embed a React/TypeScript component from a sibling repo as a WebView-hosted
bundle, not a .NET assembly:

- **SDA-19** (`SekHost/`): `campus-shared-editor-kit`'s `NotesEditor`, built via
  `npm run build:host` in that repo, tagged `host-0.1.0`.
- **SDA-24** (`DmsHost/`): `campus-direct-messaging`'s `MessageInbox`/`MessageThreadView`,
  same build, tagged `host-0.1.0`.

**Known gap carried over from the split:** `StudentDesktop.csproj`'s `<Content Include>` globs
for `dist/host/**` currently expect that output to exist at
`../../packages/{shared-editor-kit,direct-messaging}/dist/host/**` — a monorepo-relative path
that no longer resolves now that those packages are separate repos. Missing `dist/host` yields
zero copied files, not a build error (Content globs don't fail on no matches), so `dotnet build`
still succeeds, but `SekHost`/`DmsHost` will be empty until this is fixed. Fixing it requires
deciding a cross-repo distribution mechanism for .NET (git submodule pinned to the `host-0.1.0`
tag, a downloaded release asset, or similar) — this was a real open question at split time, not
yet resolved. Whoever picks this up should update `StudentDesktop.csproj` and this section.

## Code conventions

Match the surrounding code's style and MVVM folder layout. Feature IDs referenced in this repo:
SDA-01 through SDA-24 (see the architecture doc's Section 2/7 for the full feature list).

## About this repo's history

Commits from the original `Omega` monorepo that didn't touch `apps/student-desktop/` appear as
no-op entries in `git log` — a known cost of `git subtree split`, not a bug. The `CONTAINS_UP_TO_*`
tag marks the last monorepo commit included in this history.
