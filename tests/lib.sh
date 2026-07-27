#!/usr/bin/env bash
set -euo pipefail

if [[ -v testlib_sourced ]]; then
	return
fi
testlib_sourced=1

DIR="$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"
GITDIR=$(readlink -f "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"/..)

cli() {
	L_logrun "${opt_exe[@]}" "$@"
}

fatal() {
	echo "$@" >&2
	exit 123
}

# SHA-256 of the L_lib.sh asset attached to the L_lib v1.1.0 release. pull_L_lib sources
# ~11k lines of third-party bash into this shell, so it is verified before use, the same way
# the PDF generator is pinned in src/KCKSeFCli/XML2PDFCommand.cs. Bumping the release URL
# without also updating this constant is caught by clitest_l_lib_matches_pinned_sha256.
L_lib_sha256=af6771471c0dcd9068dd0f9f148780bd8069cb4b101958e063c29309e1fedd23

# Set by pull_L_lib to the file it actually sourced.
L_lib_path=

# verify_sha256 <file> <expected-hex>. Silent; the exit status is the whole answer. An absent
# file or an empty expectation is a verification failure, never a skip.
verify_sha256() {
	local file=${1:-} expected=${2:-} actual
	if [[ -z "$expected" || ! -s "$file" ]]; then
		return 1
	fi
	actual=$(sha256sum -- "$file") || return 1
	[[ "${actual%% *}" == "$expected" ]]
}

# Sources $1 if it matches the pin, and reports whether it did.
use_L_lib() {
	local candidate=$1
	if ! verify_sha256 "$candidate" "$L_lib_sha256"; then
		return 1
	fi
	L_lib_path=$candidate
	. "$candidate" -s
}

pull_L_lib() {
	local url=https://github.com/Kamilcuk/L_lib/releases/download/v1.1.0/L_lib.sh
	local cachef="$DIR"/L_lib.sh
	if [[ -v L_LIB_VERSION ]]; then
		return
	fi
	if ! hash sha256sum 2>/dev/null; then
		fatal "sha256sum is required to verify L_lib.sh before sourcing it"
	fi
	# A cached or PATH copy that does not match the pin is not trusted into this shell; fall
	# through and fetch the pinned one instead.
	if [[ -s "$cachef" ]] && use_L_lib "$cachef"; then
		echo "Using preexisting $cachef"
		return
	fi
	local onpath
	if onpath=$(command -v L_lib.sh 2>/dev/null) && use_L_lib "$onpath"; then
		echo "Using L_lib.sh from PATH"
		return
	fi
	# Download to a sibling temporary file so a failed or tampered fetch never lands on the
	# path we source from.
	local tmpf="$cachef.tmp.$$"
	# shellcheck disable=SC2064
	trap "rm -f '$tmpf'" RETURN
	if hash curl 2>/dev/null; then
		echo "Downloading L_lib.sh from $url with curl"
		curl -sSL -o "$tmpf" "$url"
	elif hash wget 2>/dev/null; then
		echo "Downloading L_lib.sh from $url with wget"
		wget -q -O "$tmpf" "$url"
	else
		fatal "Could not download or find L_lib.sh"
	fi
	if ! verify_sha256 "$tmpf" "$L_lib_sha256"; then
		# fatal exits, which skips the RETURN trap, so drop the rejected file here.
		rm -f "$tmpf"
		fatal "L_lib.sh from $url does not match the pinned SHA-256 $L_lib_sha256. Refusing to source it."
	fi
	mv -f "$tmpf" "$cachef"
	use_L_lib "$cachef" || fatal "Downloading L_lib.sh has failed"
}

testlib_main() {
	pull_L_lib

	# Disable core dumps
	ulimit -c 0

	local args=()
	# Parse command line arguments
	L_argparse dest_prefix=opt_ \
		-- -r help="Filter tests with this regex" nargs=1 eval='args+=(-k "$1")' \
		-- -k help="Filter tests with this regex" nargs=1 eval='args+=(-k "$1")' \
		-- -l nargs=0 eval='args+=(-l)' \
		-- -s nargs=0 eval='args+=(-s)' \
		-- exe nargs=remainder help="Path to the command to test" \
		---- "$@"

	if [[ -z "${opt_exe:-}" ]]; then
		if L_hash make; then
			L_logrun make -C "$DIR"/.. build
		else
			L_logrun dotnet build "$DIR"/../src/KCKSeFCli
		fi
		opt_exe=("$(readlink -f "$DIR"/../cli)")
	fi

	if [[ "$(type "${opt_exe[0]}")" == *"function"* ]]; then
		L_fatal "First argument is the executabl to test. Use -r <regex> to filter tests to execute"
	fi
	opt_exe=$(readlink -f "${opt_exe[0]}") || exit 234

	local cmd=( L_unittest_main -p clitest_ "${args[@]}" )

	# Create a global temporary directory.
	L_with_tmpdir_to TMPD
	export TMPD

	if [[ -v KCLLM ]]; then
		# When running from GEMINI, we do not need to print everything all at once every line.
		# Just give GEMINI enouhg context to work with.
		tmp=$( "${cmd[@]}" 2>&1 )
		tail -n 100 <<<"$tmp"
	else
		"${cmd[@]}"
	fi
}

testlib_setup_integration_config() {
	pull_L_lib
	if [[ -v KCLLM ]]; then
		L_fatal "Integration tests have to executed by a human"
	fi
	if [[ -z "${KCKSEFCLI_CONFIG:-}" ]]; then
		local i
		for i in \
			"$GITDIR/.git/KSEF/kcksefcli.yaml" \
			"$GITDIR/.git/kcksefcli.yaml" \
			"$GITDIR/.git/secrets/kcksefcli.yaml" \
			"$GITDIR/.git/secret/kcksefcli.yaml" \
			"$GITDIR/secrets/kcksefcli.yaml" \
		; do
			if [[ -r "$i" ]]; then
				export KCKSEFCLI_CONFIG="$(readlink -f "$i")"
				# echo "export KCKSEFCLI_CONFIG=$KCKSEFCLI_CONFIG" >&2
				break
			fi
		done
	fi
	L_assert "Could not find KCKSEFCLI for integration tests. Integration tests have to execute by a human" \
		test -n "${KCKSEFCLI_CONFIG:-}"
	L_log "Using KCKSEFCLI_CONFIG=$KCKSEFCLI_CONFIG for integration tests"
	return 0
}

# testlib_profile_nip <profile> — echoes the NIP the CLI itself resolves for that profile:
# an explicit `nip:` from the config, or the one extracted from the token or the certificate.
# PrintConfig sends its log lines to stderr and never redacts the NIP, so stdout is parseable.
testlib_profile_nip() {
	local nip
	nip=$("${opt_exe[@]}" PrintConfig -a "$1" --json 2>/dev/null |
		sed -n 's/.*"Nip":[[:space:]]*"\([0-9]*\)".*/\1/p')
	if [[ ! "$nip" =~ ^[0-9]{10}$ ]]; then
		L_fatal "Could not resolve a 10-digit NIP for profile '$1' via PrintConfig, got '$nip'"
	fi
	printf '%s\n' "$nip"
}

# testlib_make_invoice <profile> <template.xml> <output.xml> [buyer_nip]
#
# Writes <output.xml> and echoes the invoice number (P_2) it generated, so a test can search
# KSeF for exactly the invoice it just filed.
#
# Integration tests file real invoices in the name of whoever's credentials are configured, so
# the seller NIP cannot be a fixture constant: KSeF rejects an invoice whose Podmiot1 is not
# the authenticated context with 410 "Nieprawidłowy zakres uprawnień". Both substitutions are
# needed for the invoice to be accepted — P_2 because a repeated invoice number is refused as a
# duplicate, Podmiot1/NIP because of that permission check. The template keeps its own NIP so
# that the offline unit tests and tests/expected_korekta.xml stay byte-stable.
#
# The generated number is a bare timestamp deliberately: it survives SafePath.SafeFileName
# unchanged, so PobierzFaktury --useInvoiceNumber writes a filename the caller can predict.
# The template's own "FV2026/02/150" would not — the slashes become underscores.
testlib_make_invoice() {
	local profile=$1 template=$2 output=$3 buyer_nip=${4:-} nip number
	nip=$(testlib_profile_nip "$profile")
	number=$(date +%s.%N)
	# The seller substitution is confined to Podmiot1; the buyer one to Podmiot2, and only
	# when asked, because the buyer is not subject to the permission check.
	sed -e "s|<P_2>.*<|<P_2>$number<|" \
		-e "/<Podmiot1>/,/<\/Podmiot1>/ s|<NIP>[0-9]*</NIP>|<NIP>$nip</NIP>|" \
		"$template" >"$output"
	if [[ -n "$buyer_nip" ]]; then
		sed -i "/<Podmiot2>/,/<\/Podmiot2>/ s|<NIP>[0-9]*</NIP>|<NIP>$buyer_nip</NIP>|" "$output"
	fi
	printf '%s\n' "$number"
}

# The date range an integration test has to search by. The fixtures carry a fixed P_1 — 2026-02-15
# in FA_3_Przykład_1.xml — so the default dateType=Issue cannot find an invoice filed today: it
# filters on the invoice's own issue date, not on when KSeF accepted it. Invoicing is the date of
# acceptance into KSeF, which is what "the invoice I just filed" means.
testlib_recent_range() {
	printf '%s\n' --dateType Invoicing --from "$(date -u -d '1 day ago' +%Y-%m-%dT%H:%M:%S+00:00)"
}

# testlib_find_invoice <profile> <invoice_number> <subject_type> — echoes the SzukajFaktur JSON
# for that one invoice, retrying until KSeF's query API has indexed it.
#
# PrzeslijFaktury returns once the invoice is processed and has a KSeF number, but the query API
# lags behind that by a few seconds, so a single search right after filing is a coin flip. The
# empty result is exactly "[]", which is what makes the wait testable without parsing JSON.
#
# A failed search is never retried: stderr is left attached to the test log, and a non-zero exit
# aborts at once. Retrying it would spend the whole timeout turning "the token is invalid" into
# "the invoice never appeared", which is how the first version of this helper misreported a
# dateType bug.
#
# The budget is wall clock, not an attempt count, so the worst case is what it says it is —
# an attempt count silently multiplies by however long each query takes. Override with
# KCKSEFCLI_TEST_INDEX_TIMEOUT. Every attempt is logged, because L_lib buffers a test's output
# until the test ends: without this, a long wait is indistinguishable from a hang.
testlib_find_invoice() {
	local profile=$1 number=$2 subject=$3 output rc deadline attempt=0
	local -a range
	mapfile -t range < <(testlib_recent_range)
	deadline=$(($(date +%s) + ${KCKSEFCLI_TEST_INDEX_TIMEOUT:-90}))
	while :; do
		attempt=$((attempt + 1))
		rc=0
		output=$("${opt_exe[@]}" SzukajFaktur -a "$profile" -s "$subject" \
			"${range[@]}" --invoiceNumber "$number") || rc=$?
		if ((rc != 0)); then
			L_fatal "SzukajFaktur for profile '$profile' exited with $rc; see its output above"
		fi
		if [[ -n "$output" && "$output" != "[]" ]]; then
			L_log "Invoice $number found as $subject on attempt $attempt"
			printf '%s\n' "$output"
			return 0
		fi
		if (($(date +%s) >= deadline)); then
			break
		fi
		L_log "Attempt $attempt: $number not indexed yet for '$profile' as $subject, retrying"
		sleep 5
	done
	# Distinguish "not visible to this profile at all" from "the --invoiceNumber filter did not
	# match", which look identical from inside the loop and need opposite fixes.
	local unfiltered
	unfiltered=$("${opt_exe[@]}" SzukajFaktur -a "$profile" -s "$subject" "${range[@]}") || true
	if [[ "$unfiltered" == *"$number"* ]]; then
		L_fatal "Invoice $number is visible to '$profile' as $subject but --invoiceNumber '$number' does not match it"
	fi
	L_fatal "Invoice $number never became visible to profile '$profile' as $subject (unfiltered search: ${unfiltered:-<empty>})"
}

