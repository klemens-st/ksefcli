#!/usr/bin/env bash
set -euo pipefail

clitest_xml2pdf_qrcodes() {
	L_with_cd_tmpdir
	L_unittest_cmd cli XML2PDF "$DIR"/FA_3_Przykład_1.xml out.pdf --nrKSeF "1234567890-20260223-1234567890AB" --qrCode "http://someurl" --qrCode2 "https://someuerl"
	L_unittest_cmd ls -la out.pdf
}

clitest_z_integration_SprawdzLimitCertyfikatow() {
	local output
	L_unittest_cmd -v output cli SprawdzLimitCertyfikatow -a mytoken
	"$DIR"/jq_sed.sh - check <<<"$output" >/dev/null || return 1
}

# The buyer-side download path, end to end and self-contained: mytoken issues an invoice to
# token2's NIP, then token2 finds and downloads it as the buyer (-s Subject2).
#
# It files its own invoice rather than relying on account history. The previous version searched
# a fixed window in January 2026 and asserted the literal filename
# 5260215591-20260124-01006068A46A-59, which only ever held for the account that recorded it —
# every other set of credentials failed the test with nothing wrong. Searching by the invoice
# number we generated ourselves needs neither a date window nor a known KSeF number, and
# --useInvoiceNumber makes the filename on disk equally predictable.
clitest_z_integration_PobierzFaktury() {
	L_with_cd_tmpdir
	local nip2 number output
	# Breadcrumbs, because every step here can block rather than fail: a profile whose secret
	# comes from a *_cmd that prompts leaves the CLI waiting on input with no visible prompt,
	# and L_lib buffers a test's output until it ends. Run with -s to see these live.
	L_log "Resolving the NIP of profile token2"
	nip2=$(testlib_profile_nip token2)
	L_log "token2 is NIP $nip2; building an invoice from mytoken to it"
	number=$(testlib_make_invoice mytoken "$DIR"/FA_3_Przykład_1.xml faktura.xml "$nip2")
	L_log "Filing invoice $number"
	L_unittest_cmd cli PrzeslijFaktury -a mytoken faktura.xml

	output=$(testlib_find_invoice token2 "$number" Subject2)
	# -I is required whenever jq_sed.sh reads "-": L_unittest_cmd closes stdin by default
	# (it appends <&-), and the freed fd 0 is then reused by the next pipe, so the `cat` in
	# jq_sed.sh blocks forever instead of failing. The file form below needs no -I.
	L_unittest_cmd -I -v _ "$DIR"/jq_sed.sh - check <<<"$output"

	local -a range
	mapfile -t range < <(testlib_recent_range)
	L_unittest_cmd cli PobierzFaktury -a token2 -v -s Subject2 "${range[@]}" \
		--invoiceNumber "$number" --useInvoiceNumber -o . --pdf
	L_unittest_cmd ls -lah "$number".{json,pdf,xml}
	L_unittest_cmd -v _ "$DIR"/jq_sed.sh "$number".json check
}

clitest_z_integration_PrzeslijFaktury() {
	L_with_cd_tmpdir
	testlib_make_invoice mytoken "$DIR"/FA_3_Przykład_1.xml faktura1.xml
	testlib_make_invoice mytoken "$DIR"/FA_3_Przykład_1.xml faktura2.xml
	L_unittest_cmd \
		cli PrzeslijFaktury -a mytoken --upodir . --upopdf faktura1.xml faktura2.xml
	rm faktura1.xml faktura2.xml
	L_unittest_cmd ls -lah
	local xmls pdfs
	pdfs="$(find . -maxdepth 1 -name "*.pdf" | wc -l)"
	xmls="$(find . -maxdepth 1 -name "*.xml" | wc -l)"
	L_unittest_vareq xmls 2
	L_unittest_vareq pdfs 2
}

clitest_z_integration_PobierzFaktury_prod() {
	L_with_cd_tmpdir
	L_unittest_cmd -v output cli PobierzFaktury -a dyzio-prod --from 2026-02-05 --to 2026-02-05 -s Subject2 -o /tmp --pdf
}

clitest_z_integration_WystawFaktureOffline() {
	L_with_cd_tmpdir
	# Issued by mytoken's NIP, signed with the offline profile's certificate. The seller NIP
	# goes into the KOD II link as both the context and the subject identifier, and whoever
	# scans that link verifies it against the signing certificate — so the two profiles have to
	# describe the same entity. Only the certificate is taken from `offline`, which means the
	# profile needs no `nip:` of its own for this test to run.
	testlib_make_invoice mytoken "$DIR"/FA_3_Przykład_1.xml faktura_testowa.xml
	L_unittest_cmd cli WystawFaktureOffline -a offline ./faktura_testowa.xml ./faktura_testowa.pdf
	L_unittest_cmd ls -la ./faktura_testowa.pdf
}

DIR="$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"
. "$DIR"/lib.sh "$@"
testlib_setup_integration_config
testlib_main "$@"
