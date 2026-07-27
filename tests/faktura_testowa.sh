#!/bin/bash
set -euo pipefail

# Sprawdź, czy podano argument
if (($# < 1)) || (($# > 2)) || [[ -z "$1" ]]; then
  echo "Użycie: $0 <plik_wyjściowy_faktury.xml> [nip_sprzedawcy]"
  echo ""
  echo "Tworzy nowy plik XML faktury na podstawie szablonu FA_3_Przykład_1.xml,"
  echo "automatycznie aktualizując pole P_2 (data wystawienia) na aktualny timestamp."
  echo ""
  echo "Podanie <nip_sprzedawcy> podmienia NIP w Podmiot1. Bez tego faktura zostanie"
  echo "odrzucona przez KSeF z kodem 410 (nieprawidłowy zakres uprawnień), o ile NIP"
  echo "z szablonu nie jest tym, na który wystawiono token."
  exit 1
fi

output=$1
nip=${2:-}
DIR="$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"
sed "s/<P_2>.*</<P_2>$(date +%s.%N)</" "$DIR"/FA_3_Przykład_1.xml > "$output"
if [[ -n "$nip" ]]; then
  if [[ ! "$nip" =~ ^[0-9]{10}$ ]]; then
    echo "NIP musi mieć 10 cyfr, otrzymano '$nip'" >&2
    exit 1
  fi
  # Tylko Podmiot1 (sprzedawca); Podmiot2 to nabywca i nie podlega weryfikacji uprawnień.
  sed -i "/<Podmiot1>/,/<\/Podmiot1>/ s|<NIP>[0-9]*</NIP>|<NIP>$nip</NIP>|" "$output"
fi
