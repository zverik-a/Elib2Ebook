# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Elib2Ebook downloads books from many (mostly Russian-language) web libraries and saves them as epub, fb2, cbz, json, json_lite or txt. It is a .NET 9 solution (`Elib2Ebook.sln`) with three projects:

- `Core/`: all the scraping and building logic (class library).
- `Elib2EbookCli/`: console front-end (CommandLineParser). This is the main deliverable.
- `Elib2EbookWeb/`: Blazor Server front-end using MudBlazor, shipped as a Docker image.

Log messages, exceptions and comments are written in Russian. Keep new user-facing strings in Russian too.

## Commands

```bash
dotnet restore
dotnet build -c release                       # whole solution
dotnet run --project Elib2EbookCli -- -u https://author.today/work/212721 -f epub,fb2
dotnet run --project Elib2EbookCli -- -u <url> -f epub --start 3 --end 10 -s ./out
dotnet run --project Elib2EbookWeb             # web UI

# Release builds, same as CI (.github/workflows/dotnet.yml)
dotnet publish -c release -p:PublishDir=../dist/min --no-self-contained -p:PublishSingleFile=true \
  -p:DebugType=embedded -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=false --use-current-runtime Elib2EbookCli
```

### Tests and formatting

```bash
dotnet test Elib2Ebook.sln -c release
dotnet test tests/Core.Tests -c release --filter "FullyQualifiedName~SliceTocTests"   # one class
dotnet test tests/Core.Tests -c release --filter "FullyQualifiedName~SliceTocTests.NegativeStart_TakesLastChapters"   # one test
```

`tests/Core.Tests` (xUnit) holds offline unit tests:
- TOC slicing
- getter-per-host uniqueness: the `GetterProviderTests` catch two getters claiming one host
- URI and string helpers
- LibSocial annotation parsing
- retry/backoff
- EpubBuilder with and without WebP
- a `PrettyHtml` snapshot in `Fixtures/`

Core exposes internals to the tests through `InternalsVisibleTo`. Getters themselves need network access, so after changing one, also run the CLI against a real book URL from that site (for example `--end 2` to fetch only 2 chapters). The full list of CLI options is in `README.md` and `Core/Configs/Options.cs`. Options worth knowing for debugging are `--save-temp`, `--temp`, `--no-image`, `--proxy` and `--flare` (FlareSolverr URL for Cloudflare-protected sites).

Formatting follows `.editorconfig`: K&R braces, 4 spaces, no final newline (the upstream style). Most upstream files have trailing whitespace that `dotnet format` would strip, so **don't run `dotnet format` on the whole solution**. That would rewrite about 260 upstream files and make merging upstream harder. Format only the files you change:
`dotnet format whitespace Elib2Ebook.sln --include <files>`
CI checks only the `.cs` files changed relative to OnlyFart/Elib2Ebook.

Releases: CI on `master` reads `<Version>` from `Core/Core.csproj`. If no release has that tag yet, CI creates the GitHub release and pushes the Docker images. Bump that version to cut a release.

## Architecture

The pipeline is URL → **Getter** → `Book` model → **Builder(s)** → file.

1. **Config**: `BookGetterConfig.GetDefault(options, logger)` (`Core/Configs/BookGetterConfig.cs`) builds the shared `HttpClient`, `CookieContainer` and temp folder. The client uses a `SocketsHttpHandler` with HTTP/2, plus a custom `RedirectHandler`. Certificate validation is off unless `--strict-ssl` is passed. `SocketsHttpHandler` follows normal redirects itself, but it refuses HTTPS→HTTP downgrades. `RedirectHandler` follows those (up to 10) and applies the `--delay` between requests. FlareSolverr (`--flare`) wraps the whole chain. The retry helpers are in `Core/Extensions/HttpClientExtensions.cs` (`GetWithTriesAsync`, `SendWithTriesAsync`, `PostWithTriesAsync`, `GetFromJsonWithTriesAsync`). All of them go through a single `SendWithRetry`:
   - At most 5 attempts.
   - Backoff is exponential with jitter.
   - On 429 it waits for the time in `Retry-After`.
   - 400, 401, 404, 405 and 410 are returned at once without retrying.
   - 403 is retried, because anti-bot walls return it temporarily.
   - After the last attempt, the last non-success response is returned rather than thrown, so callers check `StatusCode` themselves.

2. **Getters** (`Core/Logic/Getters/`, one per site): each getter subclasses `GetterBase`. `GetterProvider` (`Core/Misc/GetterProvider.cs`) finds every non-abstract `GetterBase` subclass by reflection and picks the first one whose `IsSameUrl(url)` returns true. By default, `IsSameUrl` compares the URL's host with `SystemUrl`. Adding a site therefore only takes a new class with a `(BookGetterConfig config)` primary constructor that overrides `SystemUrl` and `Get(Uri)`. Nothing has to be registered. The lifecycle is `Init()` (sets default headers) → `Authorize()` (uses `-l`/`-p`) → `Get(url)` once per URL.
   - Helpers in `GetterBase`:
     - `SliceToc` applies `--start/--end/--start-name/--end-name`, including negative indexes counted from the end. Every getter should run its TOC through it.
     - `GetImages(HtmlDocument, Uri)` downloads `<img>` files into temp files, rewrites each `src` to the local file and drops images that fail.
     - `FillChaptersFromEpub` handles sites that serve a ready-made epub.
     - `SaveImage` sends `Referer = SystemUrl`.
   - Sites that share an engine use a common base class, for example `LibSocial/NewLibSocialGetterBase.cs` for ranobelib, mangalib and hentailib, plus subfolders such as `Litnet/`, `Freedom/` and `Fb2Top/`.
   - Site-specific JSON DTOs live in `Core/Types/<Site>/`.

3. **Model** (`Core/Types/Book/`): `Book` holds title, author/co-authors, annotation, cover, series, `Chapters` and additional files. Each `Chapter` stores its content as an HTML string and its images as a list of `TempFile`s. Images and covers are `TempFile`s (`Core/Types/Common`) stored in the temp folder. `Book.Dispose()` deletes them unless `--save-temp` is set.

4. **Builders** (`Core/Logic/Builders/`): `BuilderProvider` maps a format string to a builder. `BuilderBase.Build` handles the file name (from `--book-name-pattern` via `BookNameBuilder`), the save directory and `--cover`. Subclasses implement `BuildInternal`. `EpubBuilder` uses the vendored `Core/External/EpubSharp.dll` and the embedded templates and fonts in `Core/Patterns/`. `AdditionaFileBuilder` saves `--additional` files (original books, audio, images).

Neither front-end has any logic of its own. The CLI exit codes are 0 (success), 1 (a book failed) and 2 (bad arguments, an unsupported site, or init/auth failure). `Elib2EbookCli/Program.cs` and the Web `Components/Pages` both just call `BookGetterConfig.GetDefault` → `GetterProvider.Get` → `getter.Get` → `BuilderProvider.Get(...).Build`. All URLs passed in one CLI run must be on the same host.

## Notes

- The project uses ImageSharp 2.1.x, which is Apache-2.0 licensed and needs no license key. Only ImageSharp 3+ requires one. Never commit a personal SixLabors key (`sixlabors.lic`, `<SixLaborsLicenseKey>`). NuGet audit flags known advisories in ImageSharp 2.1.x that are only fixed in 3.x.
- `EpubBuilder` converts images to WebP only with `--webp`. It writes converted copies, because other formats in the same run share the original temp images. It then patches `media-type="image/webp"` into the `.opf`, since the vendored EpubSharp has no WebP content type.
- `Elib2EbookWeb/Books_<guid>/` directories are runtime output from the web app. They are not source; don't commit them.
