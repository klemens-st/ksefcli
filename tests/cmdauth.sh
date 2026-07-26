#!/bin/bash

# Test case: Command-line token authentication
clitest_cmd_token_auth() {
    local output
    # --reveal, because PrintConfig redacts the token by default; this test checks that the
    # command-line token reaches the active profile.
    L_unittest_cmd -v output cli PrintConfig --reveal --environment demo --token 'mytesttoken|nip-5252611332|123' --json
    L_unittest_cmd -I "$DIR"/jq_sed.sh - compare .active_profile ".__cmd__" <<< "$output"
    L_unittest_cmd -I "$DIR"/jq_sed.sh - compare .Environment "demo" <<< "$output"
    L_unittest_cmd -I "$DIR"/jq_sed.sh - compare .Nip "5252611332" <<< "$output"
    L_unittest_cmd -I "$DIR"/jq_sed.sh - compare .Token "mytesttoken|nip-5252611332|123" <<< "$output"
    L_unittest_cmd -I "$DIR"/jq_sed.sh - compare .AuthMethod "1" <<< "$output"
}

# Test case: Conflict between --config and command-line profile options
clitest_cmd_config_missing() {
	L_unittest_cmd -j -v ouptut -e 3 \
		cli PrintConfig --environment test --json
	L_unittest_cmd -j -v output -e 3 \
		cli PrintConfig --token 123 --json
}

# Test case: Conflict between --active and command-line profile options
clitest_cmd_active_conflict() {
	L_unittest_cmd -j -r "Cannot use --config or --active with command-line profile options." -e 3 \
		cli PrintConfig --active cert_test --environment test --json
}
