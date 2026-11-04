# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This repo is zverik-a's fork of [OnlyFart/Elib2Ebook](https://github.com/OnlyFart/Elib2Ebook), licensed GPL-3.0. Remotes: `origin` = the fork, `upstream` = OnlyFart. The tool downloads books from many (mostly Russian-language) web libraries and saves them as epub, fb2, cbz, json, json_lite or txt. It targets .NET 10. Package versions are managed centrally in `Directory.Packages.props`, and the shared TargetFramework and Version live in `Directory.Build.props`.

`ARCHITECTURE.md` (upstream, in Russian) describes the layers in detail. Log messages, exceptions and comments are in Russian; keep new user-facing strings in Russian.

The fork's differences from upstream are listed at the top of `README.md`. Keep that list current: GPL-3 §5a requires a notice of modifications.

## Commands

```bash
dotnet build src/Elib2EbookCli -c release           # CLI + Domain + DomainServices + all ExternalServices
dotnet run --project src/Elib2EbookCli -c release -- -u https://author.today/work/212721 -f epub --end 2 -s ./out
dotnet run --project src/Elib2EbookWeb              # Blazor web UI

dotnet test Elib2Ebook.sln -c release
dotnet test tests/Elib2Ebook.DomainServices.Tests -c release --filter "FullyQualifiedName~SliceTocTests"   # one class
dotnet format whitespace src/Elib2EbookCli/Elib2EbookCli.csproj --verify-no-changes                    # CI formatting check
```

Building `src/Elib2EbookWeb` needs the ASP.NET Core 10 targeting pack. On Arch/CachyOS that's `pacman -S aspnet-targeting-pack`; without it the build fails with `NETSDK1226`. All other projects build without it.

To check a getter, run the CLI against a real book URL from that site and use `--end 2` so only 2 chapters are fetched. The CLI exit codes are 0 (ok), 1 (a book failed) and 2 (bad args, unsupported site, or init/auth failure).

## Architecture

The pipeline is URL → **Getter** → `Book` → **Builder(s)** → file.

- `src/Elib2Ebook.Domain`: the models (`Book`, `Chapter`, `Author`, `TempFile`).
- `src/Elib2Ebook.DomainServices`: the shared logic.
  - **Config:** `Configs/BookGetterConfig` builds the shared `HttpClient`. It uses `SocketsHttpHandler` with HTTP/2, plus a `RedirectHandler` that follows HTTPS→HTTP redirects (which `SocketsHttpHandler` refuses) and applies `--delay`. FlareSolverr's `ClearanceHandler` wraps the chain when `--flare` is set.
  - **Retries:** `Extensions/HttpClientExtensions` holds the `*WithTriesAsync` helpers, which all go through a single `SendWithRetry`:
    - at most 5 attempts, with exponential backoff and jitter
    - on 429, waits for the time in `Retry-After`
    - 400, 401, 404, 405 and 410 are returned at once; 403 is retried
    - after the last attempt, the last failed response is returned rather than thrown, so callers check `StatusCode`
  - **Getters:** `Getters/GetterBase` provides `SliceToc` (the `--start/--end/--start-name/--end-name` logic, including negative indexes), `GetImages`/`SaveImage` (image requests send `Referer = SystemUrl`) and `FillChaptersFromEpub`.
  - **Builders:** `Builders/*` contains one builder per format, chosen by `Misc/BuilderProvider`.
- `src/ExternalServices/Elib2Ebook.ExternalServices.<Site>`: one project per site. Each getter subclasses `GetterBase`, overrides `SystemUrl` and `Get(Uri)`, and has a `(BookGetterConfig config)` constructor. Nothing needs registering. `Misc/GetterFactory` loads every `Elib2Ebook.ExternalServices*` assembly listed in the entry assembly's deps and picks the first getter whose `IsSameUrl` matches. The CLI references all site projects with a wildcard `ProjectReference`, so a new site project is picked up automatically.
- `LibSocial` covers ranobelib, mangalib (.me/.org), hentailib and slashlib. All of them go through `NewLibSocialGetterBase`:
  - `ApiHost`, `ImagesHost` and `SiteId` are overridable per site (hentailib has its own API and CDN hosts).
  - `--token` sets the Bearer header.
  - A chapter that fails is logged and skipped.
  - Chapters are requested by numeric `Data.Id`, falling back to `slug_url`.
- `EpubBuilder` converts images to WebP only with `--webp`:
  - It writes converted *copies*, because other formats in the same run share the original temp images.
  - It skips SVG and animated images.
  - It patches `media-type="image/webp"` into the `.opf`, since the vendored EpubSharp has no WebP type.
- ImageSharp must stay on 2.1.x (Apache-2.0). 3.x needs a Six Labors license, so don't upgrade it.

## Tests

- `tests/Elib2Ebook.DomainServices.Tests` covers:
  - upstream's tests: extensions, builders, misc
  - fork additions: `Getters/SliceTocTests`, `Http/HttpRetryTests`, `Logic/Builders/EpubBuilderTests`
- `tests/Elib2Ebook.ExternalServices.Tests/GetterFactoryTests` (fork) references all site projects through the same wildcard. It loads their assemblies first, because inside the test host the entry assembly is `testhost`. It checks that:
  - every getter is picked for its own `SystemUrl`
  - getters sharing a host (books.yandex.ru) override `IsSameUrl`
  - specific URLs resolve to the expected getter

## Style and CI

- Follow upstream's `.editorconfig`: Allman braces, 4 spaces, final newline. Nullable is disabled solution-wide, so don't use `?` on reference types.
- CI (`.github/workflows/dotnet.yml`) builds and tests on Linux, macOS and Windows, then checks formatting of `src/`.
- Releases run only in the fork (`github.repository_owner == 'zverik-a'`). They're tagged `<Version>-fork`, and the version comes from `Directory.Build.props`.
- Docker Hub push runs only if the repository variable `DOCKER_PUSH=true` is set and the `DOCKERHUB_*` secrets exist.
- To pull upstream fixes: `git fetch upstream && git merge upstream/master`.
