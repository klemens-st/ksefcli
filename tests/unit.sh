#!/usr/bin/env bash
set -euo pipefail

clitest_check_auth_nip_valid() {
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd cli CheckAuthNip -a cert_valid_nip_test
}

clitest_check_auth_nip_invalid() {
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd ! cli CheckAuthNip -a cert_invalid_nip_test
}

clitest_version() {
	L_unittest_cmd cli --version
}

clitest_help() {
	L_unittest_cmd cli --help
}

clitest_profile_cert() {
	KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd cli PrintConfig --active cert_test
}

clitest_profile_token() {
	KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd cli PrintConfig --active token_test
}

clitest_profile_env_pw() {
	TEST_PASSWORD_ENV="env_password" KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" \
	    L_unittest_cmd cli PrintConfig --active cert_env_password_test >/dev/null
    }

clitest_profile_inline() {
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd cli PrintConfig --active cert_inline_test >/dev/null
}

clitest_profile_cmd_pw() {
    local output
    # --reveal, because PrintConfig redacts secrets by default and this test exists to check
    # that password_cmd is resolved at all.
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -v output cli PrintConfig --reveal --active cert_cmd_password_test
    L_unittest_cmd -I grep -q "cmd_password_output" <<<"$output"
}

clitest_profile_cmd_pw_redacted_by_default() {
    local output
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -v output cli PrintConfig --active cert_cmd_password_test
    # The resolved secrets must not be printed. cmd_password_output still shows up inside
    # password_cmd, which is deliberately kept because it says where the secret comes from
    # without disclosing it, so match the resolved fields themselves.
    L_unittest_cmd -I grep -qx "    password: <redacted>" <<<"$output"
    L_unittest_cmd -I grep -qx "    private_key: <redacted>" <<<"$output"
}

clitest_profile_cmd_pw_conflict() {
    local output rc=0
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli_pw_conflict.yaml" cli PrintConfig --active cert_cmd_password_conflict_test 2>&1 | tee tmp.log || rc=$?
    [[ "$rc" -ne 0 ]] || fatal "Expected failure due to conflicting password configurations"
    L_unittest_cmd -I grep -q "conflicting password configurations" tmp.log
    rm tmp.log
}

clitest_profile_pk_conflict() {
    local output rc=0
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli_pk_conflict.yaml" cli PrintConfig --active cert_pk_conflict_test 2>&1 | tee tmp.log || rc=$?
    [[ "$rc" -ne 0 ]] || fatal "Expected failure due to conflicting private key configurations"
    L_unittest_cmd -I grep -q "conflicting private key configurations" tmp.log
    rm tmp.log
}

clitest_profile_cert_conflict() {
    local output rc=0
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli_cert_conflict.yaml" cli PrintConfig --active cert_cert_conflict_test 2>&1 | tee tmp.log || rc=$?
    [[ "$rc" -ne 0 ]] || fatal "Expected failure due to conflicting certificate configurations"
    L_unittest_cmd -I grep -q "conflicting certificate configurations" tmp.log
    rm tmp.log
}

clitest_help_uniewaznij() {	local output
	L_unittest_cmd -v output cli UniewaznijCertyfikat --help
	L_unittest_cmd -I grep -q "Certificate serial number to revoke" <<<"$output"
}

clitest_help_wylistuj() {
	local output
	L_unittest_cmd -v output cli WylistujCertyfikaty --help
	L_unittest_cmd -I grep -q "Filter by certificate name" <<<"$output"
}

clitest_help_pobierz() {
	local output
	L_unittest_cmd -v output cli PobierzCertyfikat --help
	L_unittest_cmd -I grep -q "Certificate serial number to retrieve" <<<"$output"
}

clitest_help_nowy() {
	local output
	L_unittest_cmd -v output cli NowyCertyfikat --help
	L_unittest_cmd -I grep -q "Name for the new certificate" <<<"$output"
}

clitest_cmd_token_test() {
    local output
	KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -v output \
		cli PrintConfig -a token_test
	KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -v output \
		cli PrintConfig -a token_no_nip_test
    }

clitest_help_qr_faktura() {
	local output
	L_unittest_cmd -v output cli --help
	L_unittest_cmd -I grep -q "QRDoFaktury                 Generate a QR code for an invoice" <<<"$output"
}

clitest_help_qr_weryfikacja() {
	local output
	L_unittest_cmd -v output cli --help
	L_unittest_cmd -I grep -q "QRWeryfikacjiFaktury        Generate a verification QR code" <<<"$output"
}

clitest_qr_weryfikacja_no_auth() {
	L_with_cd_tmpdir
	# Should fail because it needs a profile/NIP to generate the link, but we only check if the command exists and basic arg parsing
	local rc=0
	cli QRWeryfikacjiFaktury "$DIR/FA_3_Przykład_1.xml" out.png --quiet 2>/dev/null || rc=$?
	[[ "$rc" -ne 0 ]] || fatal "Expected failure due to missing authentication/profile"
}

clitest_weryfikuj_xml() {
	L_unittest_cmd cli WeryfikujXML "$DIR"/FA_3_Przykład_1.xml
}

clitest_skiasharp() {
	L_with_cd_tmpdir
	L_unittest_cmd cli TestSkiaSharp out.png
	L_unittest_cmd ls -la out.png
}

clitest_dodaj_pozycje() {
    L_with_cd_tmpdir
    cp "$DIR"/FA_3_Przykład_1.xml test_invoice.xml
    L_unittest_cmd cli DodajPozycjeNaFakturze test_invoice.xml test_invoice_out.xml \
        --nazwa "Nowa Pozycja" \
        --miara "szt" \
        --ilosc 10 \
        --cena-netto 100.00 \
        --stawka-vat 23

    local p13_1 p14_1 p15
    L_unittest_cmd -v p13_1 cli XMLExtract test_invoice_out.xml "/Faktura/Fa/P_13_1"
    L_unittest_cmd -v p14_1 cli XMLExtract test_invoice_out.xml "/Faktura/Fa/P_14_1"
    L_unittest_cmd -v p15 cli XMLExtract test_invoice_out.xml "/Faktura/Fa/P_15"

    L_unittest_vareq p13_1 "2666.66"
    L_unittest_vareq p14_1 "613.33"
    L_unittest_vareq p15 "3281.00"
}

# The 5% band, end to end. Before rate bands were mapped, only "22" and "23" were recognised:
# a 5% item left P_13_3/P_14_3 untouched and landed in P_15 with zero VAT, understating the
# invoice. The fixture starts at P_13_3=0.95, P_14_3=0.05, P_15=2051.
clitest_dodaj_pozycje_stawka_5() {
    L_with_cd_tmpdir
    cp "$DIR"/FA_3_Przykład_1.xml test_invoice.xml
    L_unittest_cmd cli DodajPozycjeNaFakturze test_invoice.xml out.xml \
        --nazwa "Pozycja 5%" --miara "szt" --ilosc 2 --cena-netto 50.00 --stawka-vat 5

    local p13_3 p14_3 p15
    L_unittest_cmd -v p13_3 cli XMLExtract out.xml "/Faktura/Fa/P_13_3"
    L_unittest_cmd -v p14_3 cli XMLExtract out.xml "/Faktura/Fa/P_14_3"
    L_unittest_cmd -v p15 cli XMLExtract out.xml "/Faktura/Fa/P_15"

    # 100.00 net at 5% is 5.00 VAT.
    L_unittest_vareq p13_3 "100.95"
    L_unittest_vareq p14_3 "5.05"
    L_unittest_vareq p15 "2156.00"

    # The 23% band must not have been touched.
    local p13_1
    L_unittest_cmd -v p13_1 cli XMLExtract out.xml "/Faktura/Fa/P_13_1"
    L_unittest_vareq p13_1 "1666.66"
}

clitest_dodaj_pozycje_stawka_nieobslugiwana() {
    L_with_cd_tmpdir
    cp "$DIR"/FA_3_Przykład_1.xml test_invoice.xml
    local output
    # "zw" has no P_13_x/P_14_x pair. It used to be treated as 0% VAT and silently added to
    # P_15 alone, leaving the invoice inconsistent.
    L_unittest_cmd -j -v output -e 1 cli DodajPozycjeNaFakturze test_invoice.xml out.xml \
        --nazwa "Zwolniona" --miara "szt" --ilosc 1 --cena-netto 10.00 --stawka-vat zw
    L_unittest_cmd -I grep -q "nie jest obsługiwana" <<<"$output"
    L_unittest_cmd -I ! test -e out.xml
}

clitest_dodaj_pozycje_brak_pol_sumujacych() {
    L_with_cd_tmpdir
    cp "$DIR"/FA_3_Przykład_1.xml test_invoice.xml
    local output
    # The fixture has no P_13_2/P_14_2, so an 8% item cannot be totalled without inserting new
    # elements in schema order. Refuse rather than unbalance the invoice.
    L_unittest_cmd -j -v output -e 1 cli DodajPozycjeNaFakturze test_invoice.xml out.xml \
        --nazwa "Pozycja 8%" --miara "szt" --ilosc 1 --cena-netto 10.00 --stawka-vat 8
    L_unittest_cmd -I grep -q "P_13_2" <<<"$output"
    L_unittest_cmd -I ! test -e out.xml
}

clitest_nowa_faktura() {
    L_with_cd_tmpdir
    L_unittest_cmd cli NowaFaktura "$DIR"/test_invoice.yaml invoice.xml
    L_unittest_cmd ls -la invoice.xml
}

clitest_nowa_faktura_nip_lookup() {
    L_with_cd_tmpdir
    L_unittest_cmd cli NowaFaktura "$DIR"/test_invoice_nip_only.yaml invoice_nip_lookup.xml

    local seller_name
    L_unittest_cmd -v seller_name cli XMLExtract invoice_nip_lookup.xml "/Faktura/Podmiot1/DaneIdentyfikacyjne/Nazwa"
    L_unittest_vareq seller_name "'KAMYK' SPÓŁKA Z OGRANICZONĄ ODPOWIEDZIALNOŚCIĄ"

    local seller_address
    L_unittest_cmd -v seller_address cli XMLExtract invoice_nip_lookup.xml "/Faktura/Podmiot1/Adres/AdresL1"
    L_unittest_vareq seller_address "LITERACKA 21/24, 01-864 WARSZAWA"
}


clitest_pobierz_info_o_nip() {
    local output
    L_unittest_cmd -v output cli PobierzInfoONip "5260202588" --data "$(date +%Y-%m-%d)"
    L_unittest_cmd -I grep -q "subject" <<<"$output"
}

clitest_xml_extract() {
    L_with_cd_tmpdir
    cp "$DIR/test_xml_extract_simple.xml" test.xml
    local output
    L_unittest_cmd -v output cli XMLExtract test.xml "/Root/Element1"
    L_unittest_vareq output "Value1"

    L_unittest_cmd -v output cli XMLExtract test.xml "/Root/Element2/NestedElement"
    L_unittest_vareq output "NestedValue"
}

clitest_xml_extract_namespace() {
    # With namespace stripping (default): plain XPath, no prefixes needed
    local output
    L_unittest_cmd -v output cli XMLExtract "$DIR/test_xml_extract.xml" "/Root/Element1"
    L_unittest_vareq output "Value1"
    L_unittest_cmd -v output cli XMLExtract "$DIR/test_xml_extract.xml" "/Root/Element2/NestedElement"
    L_unittest_vareq output "NestedValue"
    L_unittest_cmd -v output cli XMLExtract "$DIR/test_xml_extract.xml" "/Root/Info"
    L_unittest_vareq output "MetaValue"
}

clitest_xml_remove_namespace() {
    # Test case 1: From a specific namespace to default
    L_unittest_cmd cli XMLRemoveNamespace "$DIR/test_with_namespace.xml" "$TMPD/output1.xml"
    L_unittest_cmd diff -u "$DIR/test_expected_no_namespace.xml" "$TMPD/output1.xml"

    # Test case 2: From a default namespace to the same default namespace
    L_unittest_cmd cli XMLRemoveNamespace "$DIR/test_with_default_namespace.xml" "$TMPD/output2.xml"
    L_unittest_cmd diff -u "$DIR/test_expected_no_namespace.xml" "$TMPD/output2.xml"
}

clitest_wystawkorekte() {
    local INPUT_FILE="$DIR/FA_3_Przykład_1_korekta_input.xml"
    local OUTPUT_FILE="$TMPD/korekta_output.xml"
    local EXPECTED_FILE="$DIR/expected_korekta.xml"
    # Generate the correction file
    L_unittest_cmd "$opt_exe" WystawKorekte \
        "$INPUT_FILE" \
        "$OUTPUT_FILE" \
        1 5 \
        --PrzyczynaKorekty "Testowa korekta" \
        --no-validate
    # Compare the generated file with the expected one
    L_unittest_cmd diff -u "$EXPECTED_FILE" "$OUTPUT_FILE"
    rm "$OUTPUT_FILE"
}

# The production confirmation gate, end to end. The point of the gate is what happens with no
# terminal attached, which is how an agent runs, so these pipe stdin from /dev/null.
# The gate runs before authentication, so no network is involved and the fake token is never
# sent anywhere.
clitest_prod_upload_refused_without_terminal() {
    local output
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -j -v output -e 1 \
        cli PrzeslijFaktury -a token_prod "$DIR/FA_3_Przykład_1.xml" </dev/null
    L_unittest_cmd -I grep -q "Odmowa" <<<"$output"
    # Exit code 3 means "unhandled exception"; a refusal is an ordinary failure, and the
    # operator needs the message rather than a stack trace.
    L_unittest_cmd -I ! grep -q "at KCKSeFCli" <<<"$output"
}

clitest_prod_upload_allowed_with_yes() {
    local output
    # --yes gets past the gate. The command then fails at authentication with a fake token,
    # which is the proof that the gate is no longer what stopped it.
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -v output \
        ! cli PrzeslijFaktury --yes -a token_prod "$DIR/FA_3_Przykład_1.xml" </dev/null || true
    L_unittest_cmd -I ! grep -q "Odmowa" <<<"$output"
}

clitest_test_env_upload_not_gated() {
    local output
    # Non-production must not be gated at all, otherwise agents cannot work unattended.
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -v output \
        ! cli PrzeslijFaktury -a token_test "$DIR/FA_3_Przykład_1.xml" </dev/null || true
    L_unittest_cmd -I ! grep -q "Odmowa" <<<"$output"
}

clitest_prod_revoke_refused_without_terminal() {
    local output
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -j -v output -e 1 \
        cli UniewaznijCertyfikat -a token_prod 0123456789 </dev/null
    L_unittest_cmd -I grep -q "Odmowa" <<<"$output"
}

# Regression tests for the test harness itself. tests/lib.sh downloads ~11k lines of
# third-party bash (L_lib.sh) over the network and sources it into this shell. An unverified
# download that is then executed is the same defect class the PDF generator had before it was
# pinned in XML2PDFCommand.cs, only here it sits in the harness rather than the product.
clitest_verify_sha256_accepts_and_rejects() {
    local f="$TMPD/verify_sha256_probe" good
    printf 'kcksefcli\n' >"$f"
    good=$(sha256sum -- "$f"); good=${good%% *}

    L_unittest_success verify_sha256 "$f" "$good"
    # A single flipped character must be rejected.
    L_unittest_failure verify_sha256 "$f" "0${good:1}"
    # An empty expectation is a verification failure, never a skip.
    L_unittest_failure verify_sha256 "$f" ""
    # A missing file is a failure, not a pass by absence.
    L_unittest_failure verify_sha256 "$TMPD/verify_sha256_absent" "$good"
    # Tampering with the content must be rejected against the original hash.
    printf 'tampered\n' >>"$f"
    L_unittest_failure verify_sha256 "$f" "$good"
    rm "$f"
}

clitest_l_lib_matches_pinned_sha256() {
    # Set by pull_L_lib to whatever it actually sourced, so this covers the cache, the copy
    # found on PATH and a fresh download alike.
    L_unittest_success test -n "${L_lib_path:-}"
    L_unittest_success verify_sha256 "${L_lib_path:-}" "${L_lib_sha256:-}"
}

###############################################################################

DIR="$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"
. "$DIR"/cmdauth.sh
. "$DIR"/lib.sh "$@"
. "$DIR"/test_parsedate.sh
testlib_main "$@"
