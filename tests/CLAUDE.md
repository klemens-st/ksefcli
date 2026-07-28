# tests/ — conventions

Working notes for this repo's test suites, split out of the root `CLAUDE.md` so they load
only when you work under `tests/`. Everything else — gotchas, security invariants, money
math, commit rules — stays in the root file.

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
