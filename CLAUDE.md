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

## Cross-repo NativeWebView integrations (SDA-19, SEK-01, SDA-24)

Three features embed a React/TypeScript component from a sibling repo as a WebView-hosted
bundle, not a .NET assembly. Each sibling is a **git submodule** under `external/`, pinned to
its own `host-<version>` tag — `external/shared-editor-kit` and `external/direct-messaging`
tag independently, so don't assume their versions match:

- **SDA-19** (`SekHost/`) and **SEK-01** (`CodeHost/`): both come from the same submodule,
  `external/shared-editor-kit` → `campus-shared-editor-kit`'s `NotesEditor` and `CodeEditor`
  respectively, currently pinned to `host-0.4.0`.
- **SDA-24** (`DmsHost/`): `external/direct-messaging` → `campus-direct-messaging`'s
  `MessageInbox`/`MessageThreadView`, currently pinned to `host-0.1.0`.

Before `dotnet build`:

```bash
git submodule update --init
(cd external/shared-editor-kit && npm install && npm run build:host)
(cd external/direct-messaging && npm install && npm run build:host)
```

This is a manual dev prerequisite, same as pre-split — not yet wired into a cross-toolchain CI
step (tracked as a follow-up). Missing `dist/host` yields zero copied files, not a build error
(Content globs don't fail on no matches), so `dotnet build` still succeeds without it, but
`SekHost`/`CodeHost`/`DmsHost` will be empty. To bump either submodule to a newer
`host-<version>` tag: `cd external/<name> && git fetch --tags && git checkout host-<version>`,
then commit the updated submodule pointer in this repo.

## Code conventions

Match the surrounding code's style and MVVM folder layout. Feature IDs referenced in this repo:
SDA-01 through SDA-24 (see the architecture doc's Section 2/7 for the full feature list).

## About this repo's history

Commits from the original `Omega` monorepo that didn't touch `apps/student-desktop/` appear as
no-op entries in `git log` — a known cost of `git subtree split`, not a bug. The `CONTAINS_UP_TO_*`
tag marks the last monorepo commit included in this history.
