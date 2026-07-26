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
dotnet test tests/KCKSeFCli.Tests/KCKSeFCli.Tests.csproj        # 205 tests
dotnet publish src/KCKSeFCli/KCKSeFCli.csproj -c Release -r linux-x64 -f net10.0 -o dist
./tests/unit.sh ./dist/kcksefcli                                # 51 CLI tests (publish first)
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
7. `tests/L_lib.sh` is downloaded at test time and gitignored. Don't commit it. It is verified
   against a SHA-256 pin before sourcing, so `sha256sum` is now required to run the CLI suite.
8. **`-k` in `tests/unit.sh` takes one regex, not an alternation** — `-k 'a|b'` matches nothing
   and reports "No tests matched" rather than erroring.

## Test conventions

- xUnit **2.9.3** — `Assert.SkipUnless` is v3-only; use an early `return` with a comment instead.
- `tests/KCKSeFCli.Tests/TestLogging.cs` is a `[ModuleInitializer]` calling
  `Log.ConfigureLogging(quiet: true)`. Without it, anything touching the static `Log` throws
  `ArgumentNullException` — `Log.Logger` stays null until a command configures it.
- Pattern for command logic: **extract the decision into a pure static function**, test that
  directly. Avoids needing a mocked `IKSeFClient` or DI container. Used by
  `PrzeslijFakturyCommand.DetermineOutcome`, `SafePath.SafeFileName`, `PrintConfigCommand.Redact`,
  `DangerousOperation.Evaluate`, `InvoiceTotals`.
- Each security fix has a regression test whose file header explains what it defends against.
- `L_unittest_cmd` calls `hash` on argv[0], so it **cannot run a shell function** — it registers
  a failed assertion instead. Use `L_unittest_success` / `L_unittest_failure` for those.
- `L_unittest_cmd -v` captures stdout only; add `-j` to also capture stderr. Combining a leading
  `!` with `-e N` cancels out — `!` already inverts the status before the comparison.
- To test config-dependent behaviour offline, add a profile to `tests/test_kcksefcli.yaml`
  (e.g. `token_prod`). The confirmation gate runs before authentication, so a fake token never
  leaves the machine.
- When a test claims to pin a bug fix, **run it against the pre-fix binary** and confirm it
  fails there. Two of this repo's fixes had first-draft tests that passed either way.
  `git stash push <file> && dotnet publish … -o dist_old && ./tests/unit.sh ./dist_old/kcksefcli`

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
- `tests/lib.sh` **pins `L_lib.sh` by SHA-256** (`L_lib_sha256`) and verifies before sourcing.
  A cached or PATH copy that does not match is re-fetched, never sourced. Bumping the release
  URL without the hash fails `clitest_l_lib_matches_pinned_sha256`.
- `DangerousOperation` gates `PrzeslijFaktury`/`NowyCertyfikat`/`UniewaznijCertyfikat` in
  production. **Production + non-interactive + no `--yes` is a refusal** — that default is the
  whole protection, since an agent cannot answer a prompt. Unknown environment names count as
  production. Policy: `docs/BezpieczenstwoAgentow.md`.
- Retries fire **only on HTTP 429**, which KSeF returns before acting, so no wrapped call can
  be performed twice. `SendBatchPartsAsync` is deliberately unwrapped (storage URLs, not the
  rate-limited API).
- Platform-gated APIs need `[UnsupportedOSPlatformGuard("windows")]` on the guard property, or
  CA1416 fails the build; a plain `bool` property is not enough for the analyzer.

## Money math

`Utils/InvoiceTotals.cs` owns it; don't reimplement per-command.

- FA(3) has a **net/VAT field pair per rate band**: 22/23 → `P_13_1`/`P_14_1`, 7/8 → `_2`,
  5 → `_3`, 4 → `_4`. Handling only 22/23 was a real defect in two commands — other bands were
  silently dropped while `P_15` moved, so the invoice did not add up.
- Rates with **no pair at all** (0%, zw, np, oo) record their net elsewhere. Refuse rather than
  assume zero VAT.
- Round **once, before the value reaches any total**, away from zero (`Math.Round` defaults to
  banker's rounding). VAT is computed on the **band total**, not per line, or rounding error
  accumulates.
- **XSD validation does not check that the totals agree.** An invoice missing a whole band
  validates fine. Never treat `WeryfikujXML` passing as evidence the money is right.
- `WystawKorekte` replaces a corrected line with a negated copy plus the corrected one, so that
  band holds the **difference** while untouched lines keep their full value. Pre-existing
  semantic, encoded in `tests/expected_korekta.xml`. Don't "fix" it without deciding what a KOR
  invoice should state.

## Conventions

- `.editorconfig` governs: 4 spaces, max line 100, K&R braces (`csharp_new_line_before_open_brace = none`).
- Explicit types over `var`, matching surrounding code.
- **User-facing output and docs are Polish**; code comments and commit messages are English.
  This includes `[Option(HelpText = ...)]`. Existing HelpText is inconsistently English —
  don't copy a neighbour, write Polish.
- Exit codes: `0` ok, `1` failure, `2` partial success (`PrzeslijFaktury` — some invoices filed,
  so a blind retry duplicates them), `3` unhandled exception.
- Commits stay small and separable so upstreaming to the GitLab original remains cheap.
