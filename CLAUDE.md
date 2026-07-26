# kcksefcli — working notes

C# CLI for the Polish KSeF e-invoicing API, wrapping the official `CIRFMF/ksef-client-csharp`
client (git submodule). GPLv3. This fork is hardened for agentic use; see
`claude/ksef-cli-evaluation-d8qhpu`.

## Setup

A fresh checkout has **no toolchain and an empty submodule**. Both are required:

```bash
sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0   # 404 without the update first
git submodule update --init --recursive
```

Needs network to `api.nuget.org` and `github.com`.

## Build & test

```bash
dotnet build                                                   # WHOLE solution — see gotcha 1
dotnet test tests/KCKSeFCli.Tests/KCKSeFCli.Tests.csproj        # 98 tests
dotnet publish src/KCKSeFCli/KCKSeFCli.csproj -c Release -r linux-x64 -f net10.0 -o dist
./tests/unit.sh ./dist/kcksefcli                                # 40 CLI tests
dotnet run --project src/KCKSeFCli -f net10.0 -- <verb> [opts]  # -f is required
```

## Gotchas

1. **Always `dotnet build` the whole solution before committing.** Project multi-targets
   `net6.0;net10.0`. Building `-f net10.0` only hid a `net6.0` break for 8 commits
   (`SHA256.HashData(Stream)` is .NET 7+; .NET 6 has only the `byte[]` overload). Publish is
   `-f net10.0`; build is not.
2. **`make test` rewrites ~30 files** — it runs `dotnet format` without `--verify-no-changes`.
   Use `dotnet test` directly. The drift is pre-existing, mostly in verbatim copies of upstream
   client helpers (`Utils/AsyncPollingUtils.cs`, `BatchSessionUtils.cs`, `KsefRateLimitWrapper.cs`);
   reformatting them would complicate re-syncing with upstream.
3. **`TreatWarningsAsErrors=true`** — any new warning fails the build.
4. **~100 `NU1903` warnings are expected**, from the submodule declaring a vulnerable
   `System.Security.Cryptography.Xml`. Not breakage.
5. **Two CLI tests hit the live government registry** (`clitest_pobierz_info_o_nip`,
   `clitest_nowa_faktura_nip_lookup`) via `wl-api.mf.gov.pl`. They fail with no route to that
   host. They also assert **live third-party data** — `clitest_nowa_faktura_nip_lookup` expects
   NIP `5260202588` to resolve to the exact string `'KAMYK' SPÓŁKA Z OGRANICZONĄ
   ODPOWIEDZIALNOŚCIĄ` at `LITERACKA 21/24, 01-864 WARSZAWA`, so a change in that company's
   registered details breaks the test without anything being wrong here. Check the API response
   before hunting for a bug in our code.
6. **`.format_check` in `.gitlab-ci.yml` never runs** — leading dot makes it a template job.
7. `tests/L_lib.sh` is downloaded at test time and gitignored. Don't commit it.

## Test conventions

- xUnit **2.9.3** — `Assert.SkipUnless` is v3-only; use an early `return` with a comment instead.
- `tests/KCKSeFCli.Tests/TestLogging.cs` is a `[ModuleInitializer]` calling
  `Log.ConfigureLogging(quiet: true)`. Without it, anything touching the static `Log` throws
  `ArgumentNullException` — `Log.Logger` stays null until a command configures it.
- Pattern for command logic: **extract the decision into a pure static function**, test that
  directly. Avoids needing a mocked `IKSeFClient` or DI container. Used by
  `PrzeslijFakturyCommand.DetermineOutcome`, `SafePath.SafeFileName`, `PrintConfigCommand.Redact`.
- Each security fix has a regression test whose file header explains what it defends against.

## Security invariants — do not undo

Each was a real defect with a regression test; "cleaning up" any of these reintroduces it.

- `System.Security.Cryptography.Xml` is **pinned to 10.0.10** in `KCKSeFCli.csproj`. The
  submodule declares a vulnerable version; the pin is permanent.
- `XmlValidator` registers the **full vendored XSD chain** and sets `XmlResolver = null`,
  `DtdProcessing = Prohibit`. Never let it fetch a `schemaLocation`. Provenance and SHA-256s in
  `src/KCKSeFCli/Resources/README.md`. Note `XmlSchemaSet` **silently skips** an unresolvable
  import, so a broken chain degrades into "type is not declared" rather than failing loudly.
- The PDF generator is **pinned by SHA-256** (`XML2PDFCommand.LinuxGeneratorSha256` etc.) and
  verified before `chmod +x`. Cache is content-addressed, not timestamp-based.
- `PrintConfig` **redacts by default**; secrets only with `--reveal`. `ConfigLoader` resolves
  `*_file`/`*_env`/`*_cmd` into cleartext before any command sees the profile.
- Token cache is created **0600 atomically** via `FileStreamOptions.UnixCreateMode`, never
  chmod-after-write.
- KSeF-supplied identifiers go through `SafePath.SafeFileName` before use as filenames.
  `Path.Combine` discards its first argument if the second is rooted.
- **`SelfUpdate` was removed deliberately** — no stable artifact to pin. Don't reinstate.
- Platform-gated APIs need `[UnsupportedOSPlatformGuard("windows")]` on the guard property, or
  CA1416 fails the build; a plain `bool` property is not enough for the analyzer.

## Conventions

- `.editorconfig` governs: 4 spaces, max line 100, K&R braces (`csharp_new_line_before_open_brace = none`).
- Explicit types over `var`, matching surrounding code.
- **User-facing output and docs are Polish**; code comments and commit messages are English.
- Exit codes: `0` ok, `1` failure, `2` partial success (`PrzeslijFaktury` — some invoices filed,
  so a blind retry duplicates them), `3` unhandled exception.
- Commits stay small and separable so upstreaming to the GitLab original remains cheap.
