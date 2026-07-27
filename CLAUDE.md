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
dotnet test   # runs StudentDesktop.Tests via StudentDesktop.sln
```

`StudentDesktop.sln` at repo root lists only `StudentDesktop.Tests` (not the main app
project), specifically so bare `dotnet test` from repo root can't silently resolve to
`StudentDesktop.csproj` instead — a non-test project "succeeds" a `dotnet test` run with
zero tests executed and exit code 0, with no warning that nothing ran. If `dotnet test`
ever reports 0 tests, something is wrong (e.g. the .sln was deleted or a new project file
was added to root) — it should always report `50` tests passed. `dotnet build`/`dotnet run`
still build/run the main app fine: the test project's `ProjectReference` to
`StudentDesktop.csproj` pulls it into the build graph regardless of solution membership.

## Cross-repo NativeWebView integrations (SDA-19, SDA-24)

Two features embed a React/TypeScript component from a sibling repo as a WebView-hosted
bundle, not a .NET assembly. Each sibling is a **git submodule** under `external/`, pinned to
that repo's `host-0.1.0` tag:

- **SDA-19** (`SekHost/`): `external/shared-editor-kit` → `campus-shared-editor-kit`'s
  `NotesEditor`.
- **SDA-24** (`DmsHost/`): `external/direct-messaging` → `campus-direct-messaging`'s
  `MessageInbox`/`MessageThreadView`.

Before `dotnet build`:

```bash
git submodule update --init
(cd external/shared-editor-kit && npm install && npm run build:host)
(cd external/direct-messaging && npm install && npm run build:host)
```

This is a manual dev prerequisite, same as pre-split — not yet wired into a cross-toolchain CI
step (tracked as a follow-up). Missing `dist/host` yields zero copied files, not a build error
(Content globs don't fail on no matches), so `dotnet build` still succeeds without it, but
`SekHost`/`DmsHost` will be empty. To bump either submodule to a newer `host-<version>` tag:
`cd external/<name> && git fetch --tags && git checkout host-<version>`, then commit the
updated submodule pointer in this repo.

## Code conventions

Match the surrounding code's style and MVVM folder layout. Feature IDs referenced in this repo:
SDA-01 through SDA-24 (see the architecture doc's Section 2/7 for the full feature list).

## About this repo's history

Commits from the original `Omega` monorepo that didn't touch `apps/student-desktop/` appear as
no-op entries in `git log` — a known cost of `git subtree split`, not a bug. The `CONTAINS_UP_TO_*`
tag marks the last monorepo commit included in this history.
