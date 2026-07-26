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

# OPEN FINDING — fails against the current tree; passes once P_12 is written normalised.
#
# InvoiceTotals.BandForRate deliberately tolerates a trailing "%" and surrounding space, and
# InvoiceTotalsTests pins that. DodajPozycjeNaFakturze uses it for the band lookup but then
# writes P_12 from the raw --stawka-vat, so "23%" totals correctly into P_13_1/P_14_1/P_15 and
# is emitted verbatim into an element whose type (TStawkaPodatku) is a closed enumeration.
# The file is written before validation runs, so the command leaves invalid XML on disk.
clitest_dodaj_pozycje_stawka_z_procentem() {
    L_with_cd_tmpdir
    cp "$DIR"/FA_3_Przykład_1.xml test_invoice.xml
    L_unittest_cmd cli DodajPozycjeNaFakturze test_invoice.xml out.xml \
        --nazwa "Pozycja 23%" --miara "szt" --ilosc 1 --cena-netto 100.00 --stawka-vat '23%'

    # P_12 must carry the enumeration value, not whatever the operator typed.
    local p12
    L_unittest_cmd -v p12 cli XMLExtract out.xml "/Faktura/Fa/FaWiersz[last()]/P_12"
    L_unittest_vareq p12 "23"

    # And the totals must still be the ones the normalised rate implies.
    local p13_1 p14_1
    L_unittest_cmd -v p13_1 cli XMLExtract out.xml "/Faktura/Fa/P_13_1"
    L_unittest_cmd -v p14_1 cli XMLExtract out.xml "/Faktura/Fa/P_14_1"
    L_unittest_vareq p13_1 "1766.66"
    L_unittest_vareq p14_1 "406.33"
}

# OPEN FINDING — fails against the current tree; passes once the HelpText stops offering 0.
#
# 0% has no P_13_x/P_14_x pair, so the command refuses it (clitest_dodaj_pozycje_stawka_
# nieobslugiwana pins the refusal). Advertising it in --help sends the operator straight into
# that refusal. The repo convention also asks for Polish HelpText on any option touched.
clitest_dodaj_pozycje_help_nie_obiecuje_stawki_zero() {
    local output
    L_unittest_cmd -v output cli DodajPozycjeNaFakturze --help
    local stawka_line
    stawka_line=$(grep -- "--stawka-vat" <<<"$output" || true)
    [[ -n "$stawka_line" ]] || fatal "No --stawka-vat line in the help output"
    L_unittest_cmd -I ! grep -qE '(^|[ ,])0([ ,.]|$)' <<<"$stawka_line"
}

clitest_nowa_faktura() {
    L_with_cd_tmpdir
    L_unittest_cmd cli NowaFaktura "$DIR"/test_invoice.yaml invoice.xml
    L_unittest_cmd ls -la invoice.xml
}

# NowaFaktura emitted P_13_1/_2/_3 only, for rates 23, 8 and 5. A 4% position (ryczałt) was
# counted in P_15 but had no summary fields at all, so the invoice's own components summed to
# 1230.00 against a stated total of 1334.00 — and it still passed XSD validation, which does
# not check that the totals agree.
clitest_nowa_faktura_stawka_ryczalt() {
    L_with_cd_tmpdir
    L_unittest_cmd cli NowaFaktura "$DIR"/test_invoice_ryczalt.yaml out.xml

    local p13_1 p14_1 p13_4 p14_4 p15
    L_unittest_cmd -v p13_1 cli XMLExtract out.xml "/Faktura/Fa/P_13_1"
    L_unittest_cmd -v p14_1 cli XMLExtract out.xml "/Faktura/Fa/P_14_1"
    L_unittest_cmd -v p13_4 cli XMLExtract out.xml "/Faktura/Fa/P_13_4"
    L_unittest_cmd -v p14_4 cli XMLExtract out.xml "/Faktura/Fa/P_14_4"
    L_unittest_cmd -v p15 cli XMLExtract out.xml "/Faktura/Fa/P_15"

    # 1230.00 gross at 23% is 1000.00 + 230.00.
    L_unittest_vareq p13_1 "1000.00"
    L_unittest_vareq p14_1 "230.00"
    # 104.00 gross at 4% is 100.00 + 4.00. This band did not exist in the output before.
    L_unittest_vareq p13_4 "100.00"
    L_unittest_vareq p14_4 "4.00"
    # P_15 must equal the sum of the bands it reports.
    L_unittest_vareq p15 "1334.00"
}

# OPEN FINDING — fails against the current tree; passes once a negative band is still emitted.
#
# GenerateXml skips P_13_x/P_14_x for any merged band whose net is <= 0, but totalGross is
# accumulated from every position unconditionally. A rabat that drives one band negative
# therefore vanishes from the summary while still moving P_15. In this fixture the 23% band
# nets to -500.00 + -115.00 VAT and is dropped, leaving components that sum to 105.00 against
# a stated P_15 of -510.00. XSD validation passes either way — it does not check the totals.
#
# A band that nets to exactly zero is harmless (it contributes nothing to either side), so the
# guard wants to be < 0, not <= 0 — or to go away entirely.
clitest_nowa_faktura_rabat_ujemne_pasmo() {
    L_with_cd_tmpdir
    L_unittest_cmd cli NowaFaktura "$DIR"/test_invoice_rabat.yaml out.xml

    local p13_1 p14_1 p13_3 p14_3 p15
    # 615.00 - 1230.00 = -615.00 gross at 23%, i.e. -500.00 net and -115.00 VAT.
    L_unittest_cmd -v p13_1 cli XMLExtract out.xml "/Faktura/Fa/P_13_1"
    L_unittest_cmd -v p14_1 cli XMLExtract out.xml "/Faktura/Fa/P_14_1"
    L_unittest_vareq p13_1 "-500.00"
    L_unittest_vareq p14_1 "-115.00"

    # The untouched band stays as it was.
    L_unittest_cmd -v p13_3 cli XMLExtract out.xml "/Faktura/Fa/P_13_3"
    L_unittest_cmd -v p14_3 cli XMLExtract out.xml "/Faktura/Fa/P_14_3"
    L_unittest_vareq p13_3 "100.00"
    L_unittest_vareq p14_3 "5.00"

    # The whole point: P_15 must equal the sum of the bands the invoice actually reports.
    # -500.00 + -115.00 + 100.00 + 5.00 = -510.00.
    L_unittest_cmd -v p15 cli XMLExtract out.xml "/Faktura/Fa/P_15"
    L_unittest_vareq p15 "-510.00"
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
    #
    # -j is belt and braces, not a fix: the refusal goes to stderr, and a bare -v captures
    # stdout only — but only for a plain command. With a leading "!", as here, L_unittest_cmd
    # merges both streams, so this assertion was already looking at the refusal. Verified by
    # sabotaging DangerousOperation.Evaluate to refuse unconditionally: this test fails against
    # that binary with or without -j. Stating -j explicitly means the coverage no longer
    # depends on the "!" being present.
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -j -v output \
        ! cli PrzeslijFaktury --yes -a token_prod "$DIR/FA_3_Przykład_1.xml" </dev/null || true
    L_unittest_cmd -I ! grep -q "Odmowa" <<<"$output"
}

clitest_test_env_upload_not_gated() {
    local output
    # Non-production must not be gated at all, otherwise agents cannot work unattended.
    # -j for the same reason as above: explicit rather than relying on the "!" merging streams.
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -j -v output \
        ! cli PrzeslijFaktury -a token_test "$DIR/FA_3_Przykład_1.xml" </dev/null || true
    L_unittest_cmd -I ! grep -q "Odmowa" <<<"$output"
}

# OPEN FINDING — fails against the current tree; passes once --retry-attempts is bounded.
#
# --retry-attempts is a plain int with no lower bound on both PrzeslijFaktury and SzukajFaktur
# (PobierzFaktury inherits the latter). ExecuteWithRetryAsync loops `for (attempt = 1; attempt
# <= maxRetryAttempts; ...)`, so at 0 the body never runs and it falls through to
# `throw new InvalidOperationException("Nieoczekiwane zakończenie pętli powtórzeń dla ...")`,
# which Program.cs reports as exit 3 with a stack trace and no mention of the flag at fault.
#
# Note this test currently attempts authentication before getting anywhere near the retry loop,
# so until it is fixed it makes a network call. The fix is to reject the value at parse time,
# which is also what makes this test offline: it must fail before any authentication happens.
clitest_retry_attempts_zero_odrzucone() {
    local output
    KCKSEFCLI_CONFIG="$DIR/test_kcksefcli.yaml" L_unittest_cmd -j -v output -e 1 \
        cli PrzeslijFaktury -a token_test --retry-attempts 0 "$DIR/FA_3_Przykład_1.xml" </dev/null
    # The operator has to be told which option they got wrong.
    L_unittest_cmd -I grep -q "retry-attempts" <<<"$output"
    # A bad argument is an ordinary failure, not an unhandled exception.
    L_unittest_cmd -I ! grep -q "at KCKSeFCli" <<<"$output"
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

# A correction on an invoice that uses more than one VAT band. RecalculateTotals handled rate
# 23 only, so P_15 was recalculated from every line while P_13_3/P_14_3 kept its
# pre-correction value and the correction did not add up.
#
# FA_3_Przykład_1.xml has 1626.01 + 40.65 at 23% and one 5% line of 1 x 0.95. The correction
# targets the 5% line, because that is what the old code could not do: it wrote P_13_1/P_14_1
# only, so P_13_3/P_14_3 kept its pre-correction 0.95/0.05 while P_15 moved. The invoice then
# did not add up — its own components summed to 2050.99 against a stated total of 2069.94.
#
# Note the pre-existing correction semantic this pins: a corrected line is replaced by a
# negated copy plus the corrected one, so its band holds the *difference*, while untouched
# lines keep their full value. That mix is what tests/expected_korekta.xml already encodes;
# this change only fixed which fields get written and how they round.
clitest_wystawkorekte_wiele_stawek() {
    L_with_cd_tmpdir
    L_unittest_cmd cli WystawKorekte "$DIR/FA_3_Przykład_1.xml" out.xml 3 21 \
        --PrzyczynaKorekty "Korekta ilości" --no-validate

    local p13_1 p14_1 p13_3 p14_3 p15
    L_unittest_cmd -v p13_1 cli XMLExtract out.xml "/Faktura/Fa/P_13_1"
    L_unittest_cmd -v p14_1 cli XMLExtract out.xml "/Faktura/Fa/P_14_1"
    L_unittest_cmd -v p13_3 cli XMLExtract out.xml "/Faktura/Fa/P_13_3"
    L_unittest_cmd -v p14_3 cli XMLExtract out.xml "/Faktura/Fa/P_14_3"
    L_unittest_cmd -v p15 cli XMLExtract out.xml "/Faktura/Fa/P_15"

    # 23% untouched: 1626.01 + 40.65 = 1666.66, VAT 383.3318 rounded to 383.33.
    L_unittest_vareq p13_1 "1666.66"
    L_unittest_vareq p14_1 "383.33"
    # 5%: the line is replaced by -0.95 and 21 x 0.95 = 19.95, so the band holds 19.00.
    # VAT 19.00 * 5% = 0.95. This is the band the old code left at 0.95/0.05.
    L_unittest_vareq p13_3 "19.00"
    L_unittest_vareq p14_3 "0.95"
    # P_15 must equal the sum of the bands it reports: 1685.66 net + 384.28 VAT.
    L_unittest_vareq p15 "2069.94"
}

###############################################################################

DIR="$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"
. "$DIR"/cmdauth.sh
. "$DIR"/lib.sh "$@"
. "$DIR"/test_parsedate.sh
testlib_main "$@"
