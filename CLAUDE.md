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
dotnet test tests/KCKSeFCli.Tests/KCKSeFCli.Tests.csproj        # 224 tests
dotnet publish src/KCKSeFCli/KCKSeFCli.csproj -c Release -r linux-x64 -f net10.0 -o dist
./tests/unit.sh ./dist/kcksefcli                                # 61 CLI tests (publish first)
dotnet run --project src/KCKSeFCli -f net10.0 -- <verb> [opts]  # -f is required
```

The 61 come from three files: `unit.sh` (52) sources `cmdauth.sh` (3) and `test_parsedate.sh` (6).
Called with no binary path, `unit.sh` runs `make build` and tests the `cli` symlink (Debug) instead.

**`tests/integration.sh` and `tests/ci.sh` cannot be run from an agent session.**
`testlib_setup_integration_config` calls `L_fatal "Integration tests have to executed by a human"`
whenever `KCLLM` is set, before anything else. They file real invoices against the KSeF test
environment with credentials found at `.git/KSEF/kcksefcli.yaml` (or `.git/kcksefcli.yaml`,
`.git/secrets/`, `.git/secret/`, `secrets/`). `ci.sh` is `integration.sh` minus
`clitest_z_integration_PobierzFaktury_prod`, which is the one test that touches **production**.
`./run.sh` loads the same config, so it is a human's tool too.

## Layout

One file per verb, flat in `src/KCKSeFCli/` — there is no `Commands/` subdirectory.
`Utils/` holds the shared logic (`InvoiceTotals`, `SafePath`, `DangerousOperation`,
the rate-limit wrapper), `Resources/` the vendored XSD chain, `thirdparty/ksef-client-csharp`
the upstream client submodule, `docs/` one Polish `.md` per verb linked from `README.md`.

**A new verb must be added to the `commandTypes` array in `Program.cs`.** It is hand-maintained,
not a reflection scan, so a command class missing from it compiles and simply is not a verb.
Add its `docs/<Verb>.md` and a README link in the same commit — `clitest_docs_match_verbs`
now enforces all three, plus that no verb is registered twice. Exemptions are two arrays at
the top of that test (`help`, `version`, `TestSkiaSharp`; `Configuration`,
`BezpieczenstwoAgentow`, `UpstreamIssues`).

## Gotchas

1. **Always `dotnet build` the whole solution before committing.** Project multi-targets
   `net6.0;net10.0`. Building `-f net10.0` only hid a `net6.0` break for 8 commits
   (`SHA256.HashData(Stream)` is .NET 7+; .NET 6 has only the `byte[]` overload). Publish is
   `-f net10.0`; build is not. Recurred with `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`
   (.NET 8+) — check any convenience overload's availability before using it.
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
   What does run: `dotnet test`, then publish, then `unit.sh` against the published binary, then
   the same `unit.sh` plus `ci.sh` again in a `wolfi-base` image provisioned by
   `tests/setup-wolfie.sh` (that `apk add coreutils` is what supplies `sha256sum` — see gotcha 7).
   `.build` is also dot-prefixed, but legitimately: jobs `extends:` it. `.format_check` is
   extended by nothing.
7. `tests/L_lib.sh` is downloaded at test time and gitignored. Don't commit it. It is verified
   against a SHA-256 pin before sourcing, so `sha256sum` is now required to run the CLI suite.
8. **`-k` in `tests/unit.sh` takes one regex, not an alternation** — `-k 'a|b'` matches nothing
   and reports "No tests matched" rather than erroring.
9. **A verb listed twice in `commandTypes` compiles, parses and runs** — but `help <verb>`
   dies with an unhandled `InvalidOperationException` ("Sequence contains more than one
   matching element"), because `CommandLine` resolves a help verb with `SingleOrDefault`.
   The verb also appears twice in `--help`. `TestTokenAuth` was duplicated for 8 commits.

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
- `L_unittest_cmd -v` captures stdout only **for a plain command**; add `-j` to also capture
  stderr. A **leading `!` changes this**: `-v output ! cmd` captures both streams even without
  `-j`. Verified by probe, and it matters — the two production-gate tests grep a stderr-only
  message out of a `-v` capture and do work. Combining a leading `!` with `-e N` cancels out —
  `!` already inverts the status before the comparison.
- **`L_unittest_cmd` closes stdin unless you pass `-I`**, appending `<&-` to the command. Any
  command that reads stdin then **hangs rather than failing** — the freed fd 0 gets reused by the
  next pipe, so the `cat` in `jq_sed.sh - <subcommand>` blocks forever. Every `jq_sed.sh -` call
  needs `-I`; the `jq_sed.sh <file>` form does not. `-I` does not weaken the assertion: with a
  leading `!` it still rejects invalid JSON.
- **`-k` must come before the binary path.** `exe nargs=remainder` swallows everything after it,
  so `unit.sh ./dist/kcksefcli -k foo` passes `-k` to the CLI as a verb and every test fails.
- `L_array_contains` takes an **array name, not an expansion**: `L_array_contains arr "$x"`,
  never `"${arr[@]}"`. Test discovery is `L_unittest_main -p clitest_` with `compgen -A
  function`, so a `clitest_*` **array** used as a test's data table is not itself collected.
- Don't pipe inside `L_unittest_success cmd | grep -q` — the pipe binds to the assertion
  helper, not the command, so the assertion silently tests the wrong thing.
- To audit docs against the CLI, use `./dist/kcksefcli help <Verb>` — **never run the verb**.
  Diff its options against the doc's backticked ones, filtering the ten globals, which the
  docs deliberately delegate to `docs/Configuration.md`. `PobierzFaktury` likewise defers its
  search options to `SzukajFaktur`, so both show up as false positives.
- To test config-dependent behaviour offline, add a profile to `tests/test_kcksefcli.yaml`
  (e.g. `token_prod`). The confirmation gate runs before authentication, so a fake token never
  leaves the machine.
- **An integration test must never hardcode a NIP, an invoice number or a date window.** It
  runs against whichever credentials the human has, and KSeF refuses an invoice whose `Podmiot1`
  is not the authenticated context (410 "Nieprawidłowy zakres uprawnień"). Build invoices with
  `testlib_make_invoice <profile> <template> <out> [buyer_nip]` in `tests/lib.sh`: it substitutes
  the seller NIP that `testlib_profile_nip` resolves from the profile (`PrintConfig --json`,
  which resolves it from an explicit `nip:`, the token, or the certificate), stamps a unique
  `P_2`, and echoes that number. Then find the invoice with `testlib_find_invoice` — the query
  API lags PrzeslijFaktury by seconds, so it polls until the result stops being `[]`. The
  fixtures keep their own NIPs; `tests/expected_korekta.xml` and the byte-comparison tests
  depend on them.
- **Searching for an invoice you just filed needs `--dateType Invoicing`**, which
  `testlib_recent_range` supplies. `SzukajFaktur` defaults to `Issue`, i.e. the invoice's own
  `P_1` — fixed at `2026-02-15` in `FA_3_Przykład_1.xml` — so a window around now silently
  matches nothing. `Invoicing` is the date KSeF accepted it. A search that returns `[]` for this
  reason looks exactly like one that returns `[]` because the credentials cannot see the
  invoice, so never retry a *failed* `SzukajFaktur`: let a non-zero exit abort immediately.
- When a test claims to pin a bug fix, **run it against the pre-fix binary** and confirm it
  fails there. Two of this repo's fixes had first-draft tests that passed either way.
  **Commit first, then** `git checkout <pre-fix-sha> -- src/ && dotnet publish … -o dist_old &&
  git checkout HEAD -- src/`. That silently discards uncommitted `src/` changes. Avoid
  `git stash`: the stack is shared with every other worktree and concurrent session.

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
- `Log.Flush()` runs in a `finally` **nested inside** `Program.Main`'s `try`, so it precedes the
  `catch` handlers — those use unbuffered `Console.Error`, so flushing after them prints a stack
  trace ahead of the log lines leading to it. `ConfigureLogging` disposes the previous factory
  (commands configure twice). Without both, the CLI intermittently exits printing **nothing**;
  the rate scales with machine load (~6% idle, 67% loaded).
- `InvoiceTotals.Summarize` keys bands by **field pair, not percent**, and sums VAT computed per
  rate. Keying by percent aimed two totals at one element; deriving VAT from the merged net
  turns 45.00 into 46.00.

## Money math

`Utils/InvoiceTotals.cs` owns it; don't reimplement per-command.

- FA(3) has a **net/VAT field pair per rate band**: 22/23 → `P_13_1`/`P_14_1`, 7/8 → `_2`,
  5 → `_3`, 4 → `_4`. Handling only 22/23 was a real defect in two commands — other bands were
  silently dropped while `P_15` moved, so the invoice did not add up.
- **0% is a rate, not a special case — don't lump it with zw/oo/np.** It has a net field and no
  VAT field, correctly, since 0% of anything is zero. `TStawkaPodatku` has no bare `0`: only
  `0 KR` (krajowa), `0 WDT` and `0 EX` (xsd:1876-1890), and each maps to exactly one field —
  `P_13_6_1`/`_2`/`_3` (xsd:2591-2605). The rate carries the transaction type, so the mapping is
  a lookup, not a guess. `InvoiceTotals.ZeroRateBandFor` owns it. Bare `0` is refused because the
  schema has no such value, not because 0% is unsupported.
- `zw` → `P_13_7`, `np I`/`np II` → `P_13_8`/`P_13_9`, `oo` — each a single net field, but each
  also carries `Adnotacje` consequences (`P_18` for `oo`). Still refused by
  `DodajPozycjeNaFakturze`; that refusal is a real limitation, not a correctness position.
- `BandForRate` returns null for **all** of the above. Its contract is "rates with a net/VAT
  pair", so null means "no pair", not "invalid rate". Don't widen it — check
  `ZeroRateBandFor` alongside it, the way `DodajPozycjeNaFakturze` does.
- Round **once, before the value reaches any total**, away from zero (`Math.Round` defaults to
  banker's rounding). VAT is computed on the **band total**, not per line, or rounding error
  accumulates.
- **XSD validation does not check that the totals agree.** An invoice missing a whole band
  validates fine. Never treat `WeryfikujXML` passing as evidence the money is right.
- `WystawKorekte` replaces a corrected line with a negated copy plus the corrected one, so that
  band holds the **difference** while untouched lines keep their full value. Pre-existing
  semantic, encoded in `tests/expected_korekta.xml`. Don't "fix" it without deciding what a KOR
  invoice should state.
- `NowaFakturaCommand` keeps its own inline band merge on purpose: it derives VAT as
  `brutto - net` so net + VAT equals the gross exactly. Routing it through `VatFor` changes the
  numbers. It is not dead duplication of `Summarize`.

## Known-open findings, and non-findings

From the review of the hardening branch. Recorded so they are neither lost nor rediscovered.

**`NowaFaktura` still under-declares 0%, zw, np and oo — needs a proper fix.** It groups by rate,
maps through `BandForRate`, and for anything without a net/VAT pair adds the net to `totalGross`
(so it reaches `P_15`) while emitting no `P_13_x` at all — it only logs a warning. The sale ends
up declared in the gross total and in no band field, which is an invalid invoice that XSD
validation will not catch. This is inherited from main, not introduced by the fork; the fork only
made it audible. `DodajPozycjeNaFakturze` now does this correctly for the three 0% variants via
`InvoiceTotals.ZeroRateBandFor` — `NowaFaktura` should route through the same helper, and needs
`P_13_7`/`P_13_8`/`P_13_9` support plus the `Adnotacje` consequences (`P_18` for `oo`) to cover
the rest. Bigger than it looks: the yaml input has no field for the transaction type that picks
between `0 KR`/`0 WDT`/`0 EX`.
`docs/NowaFaktura.md` now warns users off those rates until this is fixed.

**Still open, deliberately untested** — a test would have to be written against a helper that
does not exist yet, so write it alongside the fix:

- `PobierzFaktury` filename collisions. `SafePath.SafeFileName` is many-to-one, so two invoices
  can land on one path and the second silently overwrites the first. `SafeFileName` is right to
  be many-to-one; the fix belongs at the call site, as a disambiguator applied when the target
  already exists. The command itself needs the network and real invoices. Documented as a
  known limitation in `docs/PobierzFaktury.md`.
- `Downloader`'s delete-then-move window. `File.Delete` then `File.Move` is not atomic, so a
  failure between them loses an already-verified cached generator. Reaching it needs a
  filesystem seam the code does not have, and the fix — `File.Move(temp, dest, overwrite: true)`
  — removes the window rather than handling it, leaving nothing to assert on.

**Retracted on investigation — do not "rediscover" these:**

- That `InvoiceTotals.Bands` is missing an entry for rate 3, "the historical second reduced rate
  pairing with `P_13_3`". There is no 3% VAT rate in Polish tax law. `TStawkaPodatku` does list
  3 among the values `P_12` accepts, but no `P_13_x` pair is documented against it anywhere. The
  pairing was invented; the absence of 3 from `Bands` is correct. Rates with no pair are refused
  by design.
- That `clitest_prod_upload_allowed_with_yes` and `clitest_test_env_upload_not_gated` are
  vacuous because they grep a stderr-only refusal out of a stdout-only capture. The stream claim
  is right, the conclusion is wrong: `L_unittest_cmd` merges both streams when the command is
  prefixed with `!`, which both tests do. Sabotaging `DangerousOperation.Evaluate` to refuse
  unconditionally makes both fail, with or without `-j`.

## Conventions

- `.editorconfig` governs: 4 spaces, max line 100, K&R braces (`csharp_new_line_before_open_brace = none`).
- Explicit types over `var`, matching surrounding code.
- **User-facing output and docs are Polish**; code comments and commit messages are English.
  This includes `[Option(HelpText = ...)]`. Existing HelpText is inconsistently English —
  don't copy a neighbour, write Polish.
- Exit codes: `0` ok, `1` failure, `2` partial success (`PrzeslijFaktury` — some invoices filed,
  so a blind retry duplicates them), `3` unhandled exception.
- Option range checks go in `IGlobalCommand.ValidateOptions()`, which `Program.cs` calls between
  `ConfigureLogging` and `ExecuteAsync` — ahead of the config file, DI and authentication, so a
  bad value fails offline. `CommandLineParser` cannot bound a numeric option itself.
- Commits stay small and separable so upstreaming to the GitLab original remains cheap.
