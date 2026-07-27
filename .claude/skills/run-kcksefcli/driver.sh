#!/usr/bin/env bash
# Build and drive the kcksefcli binary. Agent tooling, not product surface.
#
#   driver.sh deps      report on toolchain and submodule, print the fix
#   driver.sh build     submodule init + BOTH-TFM build gate + publish -> dist/
#   driver.sh publish   publish only, skipping the net6.0 gate (fast iteration)
#   driver.sh smoke     end-to-end drive of dist/kcksefcli
#   driver.sh test      dotnet test + tests/unit.sh against dist/kcksefcli
#   driver.sh all       build + smoke
#
# Env:
#   KCKSEFCLI_BIN   binary to smoke (default: <repo>/dist/kcksefcli)
#   SMOKE_OUT       artifact dir   (default: <repo>/dist/smoke, gitignored)
#   RID             publish runtime id (default: linux-x64)

set -euo pipefail

HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO=$(cd "$HERE/../../.." && pwd)
cd "$REPO"

BIN=${KCKSEFCLI_BIN:-$REPO/dist/kcksefcli}
OUT=${SMOKE_OUT:-$REPO/dist/smoke}
RID=${RID:-linux-x64}

fail=0
say()  { printf '\n\033[1m== %s\033[0m\n' "$*"; }
ok()   { printf '  \033[32mok\033[0m   %s\n' "$*"; }
bad()  { printf '  \033[31mFAIL\033[0m %s\n' "$*"; fail=1; }
die()  { printf '\033[31mfatal:\033[0m %s\n' "$*" >&2; exit 1; }

# assert_eq <label> <expected> <actual>
assert_eq() {
    if [[ "$2" == "$3" ]]; then ok "$1 = $3"; else bad "$1: expected '$2', got '$3'"; fi
}
# assert_has <label> <needle> <haystack>
assert_has() {
    if [[ "$3" == *"$2"* ]]; then ok "$1"; else bad "$1: '$2' not in output"; fi
}
assert_lacks() {
    if [[ "$3" != *"$2"* ]]; then ok "$1"; else bad "$1: '$2' unexpectedly present"; fi
}
assert_file() {
    if [[ -s "$2" ]]; then ok "$1 ($(stat -c%s "$2") B)"; else bad "$1: $2 missing or empty"; fi
}

# run_msbuild <logname> <cmd...> — full log to dist/<logname>.log, errors surfaced on failure.
run_msbuild() {
    local name=$1; shift
    mkdir -p dist
    local log="dist/$name.log"
    if "$@" >"$log" 2>&1; then
        grep -E 'Warning\(s\)|Error\(s\)|-> .*/dist/' "$log" | tail -4
    else
        grep -E ' error |error [A-Z]+[0-9]+' "$log" | sort -u | head -20
        die "$name failed — full log: $log"
    fi
}

###############################################################################

cmd_deps() {
    say "toolchain"
    if command -v dotnet >/dev/null; then
        ok "dotnet $(dotnet --version)"
    else
        bad "dotnet missing -> sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0"
    fi
    dotnet --list-sdks 2>/dev/null | grep -q '^10\.' \
        || bad "no 10.x SDK; the project multi-targets net6.0;net10.0 and needs the 10 SDK"
    say "submodule"
    if [[ -f thirdparty/ksef-client-csharp/Directory.Build.props ]]; then
        ok "thirdparty/ksef-client-csharp populated"
    else
        bad "submodule empty -> git submodule update --init --recursive"
    fi
    say "optional"
    command -v convert >/dev/null && ok "ImageMagick convert (PDF -> PNG in smoke)" \
        || echo "  --   convert absent; smoke will skip the PNG render"
    command -v sha256sum >/dev/null && ok "sha256sum (required by tests/unit.sh)" \
        || bad "sha256sum missing -> apk add coreutils / apt-get install coreutils"
    return $fail
}

cmd_build() {
    say "submodule"
    [[ -f thirdparty/ksef-client-csharp/Directory.Build.props ]] \
        || git submodule update --init --recursive
    ok "populated"

    # THE GATE. Publish is -f net10.0 only, so a .NET 7+/8+ API sails through it and breaks
    # net6.0 silently. Build the whole solution, both TFMs, before you trust a dist binary.
    # The ~100 NU1903 warnings are expected (vulnerable transitive dep declared by the
    # submodule); they are filtered out so a real error is visible.
    say "build (net6.0 + net10.0, whole solution)"
    run_msbuild build dotnet build

    cmd_publish
}

cmd_publish() {
    # CI publishes every RID to -o dist (separate jobs). Here the default RID keeps dist/ so
    # tests/unit.sh finds it, and any other RID goes to dist/<rid>/ — still inside the one
    # gitignored path, since .gitignore has "dist" and not "dist*".
    local outdir=dist exe=kcksefcli
    [[ $RID != linux-x64 ]] && outdir=dist/$RID
    [[ $RID == win-* ]] && exe=kcksefcli.exe
    say "publish -> $outdir/ ($RID)"
    run_msbuild publish dotnet publish src/KCKSeFCli/KCKSeFCli.csproj \
        -c Release -r "$RID" -f net10.0 -o "$outdir"
    [[ -f $outdir/$exe ]] || die "$outdir/$exe not produced — see dist/publish.log"
    if [[ $RID == win-* ]]; then
        ok "$outdir/$exe $(stat -c%s "$outdir/$exe") B (not runnable here)"
    else
        ok "$outdir/$exe $(stat -c%s "$outdir/$exe") B — $(./$outdir/$exe --version | head -1)"
    fi
}

cmd_smoke() {
    [[ -x "$BIN" ]] || die "$BIN not found; run: $0 build"
    rm -rf "$OUT"; mkdir -p "$OUT"
    local o
    printf 'binary: %s\nout:    %s\n' "$BIN" "$OUT"

    say "1. version"
    assert_has "--version prints a version" "kcksefcli 1." "$("$BIN" --version)"

    # Every verb takes its file arguments POSITIONALLY. --input/--output do not exist.
    say "2. ParseDate (pure, no config, no network)"
    assert_eq "ParseDate 2026-02-15" "2026-02-15T00:00:00.000000" "$("$BIN" ParseDate 2026-02-15)"

    say "3. WeryfikujXML against the vendored XSD chain"
    o=$("$BIN" WeryfikujXML tests/FA_3_Przykład_1.xml 2>&1) \
        && assert_has "fixture validates" "validation successful" "$o" \
        || bad "WeryfikujXML exited non-zero: $o"

    say "4. NowaFaktura: YAML -> FA(3) XML"
    "$BIN" NowaFaktura tests/test_invoice.yaml "$OUT/faktura.xml" >/dev/null 2>&1 \
        || bad "NowaFaktura failed"
    assert_file "faktura.xml" "$OUT/faktura.xml"
    assert_eq "P_15 (kwota naleznosci ogolem)" "2230.00" \
        "$("$BIN" XMLExtract "$OUT/faktura.xml" '//*[local-name()="P_15"]')"

    say "5. DodajPozycjeNaFakturze: 2 x 100.00 netto @ 23%"
    "$BIN" DodajPozycjeNaFakturze --nazwa "Usługa testowa" --miara szt --ilosc 2 \
        --cena-netto 100 --stawka-vat 23 "$OUT/faktura.xml" "$OUT/faktura2.xml" >/dev/null 2>&1 \
        || bad "DodajPozycjeNaFakturze failed"
    # 2230.00 + 200.00 net + 46.00 VAT. VAT is computed on the band total, not per line.
    assert_eq "P_15 after adding the item" "2476.00" \
        "$("$BIN" XMLExtract "$OUT/faktura2.xml" '//*[local-name()="P_15"]')"

    say "6. PrintConfig redacts by default"
    o=$("$BIN" PrintConfig --config tests/test_kcksefcli.yaml --active cert_test --json 2>/dev/null)
    assert_lacks "no cleartext password without --reveal" "testpassword123" "$o"
    assert_has  "redaction marker present" "redacted" "$o"
    o=$("$BIN" PrintConfig --config tests/test_kcksefcli.yaml --active cert_test --json --reveal 2>/dev/null)
    assert_has  "--reveal does reveal" "testpassword123" "$o"

    say "7. production gate refuses an agent (no terminal, no --yes)"
    # Runs before authentication, so the fake prod token never leaves the machine.
    o=$(KCKSEFCLI_CONFIG=tests/test_kcksefcli.yaml "$BIN" PrzeslijFaktury \
        -a token_prod tests/FA_3_Przykład_1.xml </dev/null 2>&1) && rc=0 || rc=$?
    assert_eq  "exit code (1 = ordinary failure, not 3)" "1" "$rc"
    assert_has "refusal message" "Odmowa" "$o"
    assert_lacks "no stack trace on a refusal" "at KCKSeFCli" "$o"

    say "8. XML2PDF (downloads a SHA-256-pinned generator on first run: ~74 MB, ~35 s)"
    if "$BIN" XML2PDF "$OUT/faktura.xml" "$OUT/faktura.pdf" >"$OUT/xml2pdf.log" 2>&1; then
        assert_file "faktura.pdf" "$OUT/faktura.pdf"
        assert_has "PDF magic" "%PDF" "$(head -c4 "$OUT/faktura.pdf")"
    else
        bad "XML2PDF failed (needs network on a cold cache) — see $OUT/xml2pdf.log"
    fi

    say "9. render page 1 to PNG so an agent can LOOK at the invoice"
    if command -v convert >/dev/null && [[ -s "$OUT/faktura.pdf" ]]; then
        convert -density 110 "$OUT/faktura.pdf[0]" -background white -flatten "$OUT/faktura.png" \
            && assert_file "faktura.png — open this with Read" "$OUT/faktura.png"
    else
        echo "  --   skipped (no ImageMagick, or no PDF)"
    fi

    say "smoke result"
    if [[ $fail -eq 0 ]]; then ok "all checks passed; artifacts in $OUT"; else bad "see above"; fi
    return $fail
}

cmd_test() {
    say "dotnet test (224 xUnit tests)"
    dotnet test tests/KCKSeFCli.Tests/KCKSeFCli.Tests.csproj 2>&1 | grep -vE 'NU1903' | tail -5
    [[ -x "$BIN" ]] || die "$BIN not found; run: $0 build"
    say "tests/unit.sh against $BIN (60 black-box CLI tests, ~46 s)"
    # Two of these hit the live registry at wl-api.mf.gov.pl and assert live third-party data.
    ./tests/unit.sh "$BIN" 2>&1 | tail -3
}

###############################################################################

case "${1:-all}" in
    deps)    cmd_deps ;;
    build)   cmd_build ;;
    publish) cmd_publish ;;
    smoke)   cmd_smoke ;;
    test)    cmd_test ;;
    all)     cmd_build && cmd_smoke ;;
    *)       die "unknown command '$1' (deps|build|publish|smoke|test|all)" ;;
esac
exit $fail
