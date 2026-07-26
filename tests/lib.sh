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

