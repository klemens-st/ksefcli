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

clitest_z_integration_PobierzFaktury() {
	L_unittest_cmd -v output cli SzukajFaktur -a token2 -v --from 2026-01-21T00:00:00+01:00 --to 2026-01-22T00:00:00+01:00
	L_unittest_cmd -I -r '[12]' "$DIR"/jq_sed.sh - length <<<"$output"
	#
	L_with_cd_tmpdir
	L_unittest_cmd cli PobierzFaktury -a token2 -v --from 2026-01-21T00:00:00+01:00 --to 2026-01-22T00:00:00+01:00 -o . --pdf
	L_unittest_cmd ls -lah 5260215591-20260124-01006068A46A-59.{json,pdf,xml}
	L_unittest_cmd -v _ "$DIR"/jq_sed.sh 5260215591-20260124-01006068A46A-59.json check
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
