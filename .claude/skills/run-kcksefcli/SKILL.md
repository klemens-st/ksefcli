---
name: run-kcksefcli
description: Build the dist binary and drive kcksefcli. Use when asked to build, publish, or compile kcksefcli, produce dist/kcksefcli, run or smoke-test the CLI, generate an invoice XML or PDF, or check that a change works in the real binary rather than only in the test suite.
---

`kcksefcli` is a self-contained single-file CLI (~45 MB, `PublishSingleFile` +
`SelfContained`). Building it means one `dotnet publish` — but publishing alone
is **not** a build gate, see [Gotchas](#gotchas). Do everything through
`.claude/skills/run-kcksefcli/driver.sh`; it wraps the build, the both-TFM gate,
and an offline end-to-end drive of the resulting binary.

All paths below are relative to the repo root.

## Prerequisites

```bash
sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0   # 404 without the update first
```

`coreutils` (for `sha256sum`) is required by `tests/unit.sh`. ImageMagick
(`convert`) is optional — the driver uses it to render the generated PDF to a PNG
you can actually look at. Check everything at once:

```bash
./.claude/skills/run-kcksefcli/driver.sh deps
```

Network to `api.nuget.org`, `github.com` (the submodule and the pinned PDF
generator) and `wl-api.mf.gov.pl` (two CLI tests) is needed.

## Setup

A fresh checkout has an **empty submodule**. `driver.sh build` initialises it for
you; standalone it is:

```bash
git submodule update --init --recursive
```

## Build

```bash
./.claude/skills/run-kcksefcli/driver.sh build
```

Roughly 30 s warm, ~2 min on a cold NuGet cache. It runs, in order:

1. `git submodule update --init --recursive` if `thirdparty/ksef-client-csharp` is empty
2. `dotnet build` — **whole solution, both `net6.0` and `net10.0`**; this is the gate
3. `dotnet publish src/KCKSeFCli/KCKSeFCli.csproj -c Release -r linux-x64 -f net10.0 -o dist`

Output: `dist/kcksefcli`. Full msbuild output goes to `dist/build.log` and
`dist/publish.log`; on failure the driver prints the deduplicated `error` lines
and points at the log. `dist/` is gitignored.

Use `driver.sh publish` to skip the net6.0 gate while iterating — but not before
committing.

Cross-publish for Windows works from Linux (this is what CI does; ~54 s the first
time, while the win runtime pack downloads):

```bash
RID=win-x64 ./.claude/skills/run-kcksefcli/driver.sh publish   # -> dist/win-x64/kcksefcli.exe
```

Non-default RIDs go to `dist/<rid>/` rather than `dist/`, so `tests/unit.sh` keeps
finding the linux binary and `.gitignore`'s single `dist` entry still covers it.

## Run (agent path)

```bash
./.claude/skills/run-kcksefcli/driver.sh smoke
```

Drives the published binary through a real end-to-end flow with assertions, no
KSeF credentials and no live API. ~40 s warm; the first ever run adds ~35 s for
the PDF generator download. What it checks:

| step | what it drives |
|---|---|
| 1 | `--version` |
| 2 | `ParseDate 2026-02-15` → `2026-02-15T00:00:00.000000` |
| 3 | `WeryfikujXML` a fixture against the vendored XSD chain |
| 4 | `NowaFaktura` YAML → FA(3) XML, `XMLExtract` asserts `P_15` = `2230.00` |
| 5 | `DodajPozycjeNaFakturze` 2 × 100.00 @ 23% → `P_15` = `2476.00` (VAT on the band total) |
| 6 | `PrintConfig` redacts by default, reveals only with `--reveal` |
| 7 | production gate: `PrzeslijFaktury -a token_prod </dev/null` exits **1** with `Odmowa` and no stack trace |
| 8 | `XML2PDF` → a real PDF via the SHA-256-pinned generator |
| 9 | `convert` renders page 1 to PNG |

Artifacts land in `dist/smoke/` — `faktura.xml`, `faktura2.xml`, `faktura.pdf`,
**`faktura.png`**. Open the PNG with the Read tool; that is the closest this
project has to a screenshot, and it is the only way to see that the invoice
actually renders.

Steps 6 and 7 are regression checks on security invariants (see
`CLAUDE.md` → *Security invariants*), not decoration. Verified to have teeth:
rebuilding with `DangerousOperation.Evaluate` short-circuited to `NotRequired`
turns step 7 into three failures and the driver exits 1.

Point the smoke at another binary — e.g. one published from a pre-fix commit — with:

```bash
KCKSEFCLI_BIN=/path/to/other/kcksefcli SMOKE_OUT=/tmp/other-smoke \
  ./.claude/skills/run-kcksefcli/driver.sh smoke
```

`driver.sh all` = `build` then `smoke`.

### Driving it by hand

Every verb takes its file arguments **positionally**. There is no `--input` /
`--output`; passing them prints the help and exits 1.

```bash
./dist/kcksefcli --help                                     # verb list
./dist/kcksefcli help NowaFaktura                           # options for one verb
./dist/kcksefcli NowaFaktura tests/test_invoice.yaml out.xml
./dist/kcksefcli XMLExtract out.xml '//*[local-name()="P_15"]'
./dist/kcksefcli XML2PDF out.xml out.pdf
```

## Test

```bash
./.claude/skills/run-kcksefcli/driver.sh test
```

`dotnet test` (224 xUnit tests, <1 s) then `tests/unit.sh` against `dist/kcksefcli`
(60 black-box CLI tests, ~50 s). Both pass clean here.

`tests/integration.sh` and `tests/ci.sh` **cannot be run from an agent session** —
they abort immediately when `KCLLM` is set, and they file real invoices.

## Gotchas

- **`dotnet publish` is not a build.** Publish is `-f net10.0`; the project
  multi-targets `net6.0;net10.0`. A .NET 7+/8+ API compiles and publishes fine
  and breaks `net6.0` silently — reproduced here by adding
  `ArgumentOutOfRangeException.ThrowIfNegativeOrZero` (.NET 8+) to
  `Utils/SafePath.cs`: publish exited **0**, `dotnet build` failed with
  `CS0117 … [TargetFramework=net6.0]`. `driver.sh build` runs the whole-solution
  build first and refuses to publish if it fails. `SHA256.HashData(Stream)`
  (.NET 7+) hid the same way for 8 commits.
- **~100 `NU1903` warnings are expected**, from the submodule declaring a
  vulnerable `System.Security.Cryptography.Xml`. Not breakage. The driver filters
  them so a real error is visible; `TreatWarningsAsErrors=true` means anything
  *new* does fail the build.
- **There is no `make test` target** — it was removed because it depended on
  `format`, which runs `dotnet format` without `--verify-no-changes` and rewrites
  ~30 files. Run `dotnet test` directly. `make build` targets Debug and the `cli`
  symlink, not `dist/`.
- **First `XML2PDF` downloads ~74 MB** from `github.com/Kamilcuk/ksef-pdf-generator`
  (~35 s) into `~/.cache/kcksefcli/`, verifies it against a SHA-256 pinned in
  `XML2PDFCommand`, then `chmod +x`. Subsequent runs are ~1 s. Deleting that cache
  dir is how you re-test the cold path.
- **The rendered PDF shows a known-open bug, not a build failure.** In
  `dist/smoke/faktura.png` the second line (`Usługa OO`, 1000.00) is in the
  2230.00 total but absent from *Podsumowanie stawek podatku* — `NowaFaktura`
  under-declares `oo`/`zw`/`np`/0% bands. Documented in `CLAUDE.md` →
  *Known-open findings*. Don't chase it as a regression.
- **`tests/unit.sh` needs `-k` before the binary path.** `exe nargs=remainder`
  swallows everything after the path, so `unit.sh ./dist/kcksefcli -k foo` passes
  `-k` to the CLI as a verb and every test fails. `-k` also takes one regex, not
  an alternation.
- **Two CLI tests hit the live government registry** (`wl-api.mf.gov.pl`) and
  assert live third-party company data. They fail with no route to that host, and
  can break because a real company changed its registered address.

## Troubleshooting

- **`ERROR(S): Option 'input' is unknown.`** — the verb takes positional
  arguments. `./dist/kcksefcli help <Verb>` lists them as `value pos. 0`, `pos. 1`.
- **`Active profile not specified in config file or via --active option.`**
  followed by a stack trace, exit 3 — pass `--active <profile>`. Profile names
  live under `profiles:` in `tests/test_kcksefcli.yaml` (`cert_test`, `token_test`,
  `token_prod`, …); there is no profile literally called `test`.
- **`dist/kcksefcli not produced`** — publish succeeded but wrote nowhere useful;
  check `dist/publish.log`. Usually a wrong `RID`.
- **`error CS0117: 'X' does not contain a definition for 'Y' … [TargetFramework=net6.0]`** —
  you used an API newer than .NET 6. Check the overload's availability before
  reaching for a convenience method.
- **A newly added verb doesn't appear in `--help`** — add it to the hand-maintained
  `commandTypes` array in `src/KCKSeFCli/Program.cs`; nothing scans for it.
