[Powrót do strony głównej](../README.md)

# CheckAuthNip

Sprawdza czy NIP wyciągnięty z uwierzytelnienia (tokenu lub certyfikatu) zgadza się z NIP-em podanym w konfiguracji.
Jeśli NIP nie został podany w konfiguracji, polecenie wypisuje NIP wyciągnięty z uwierzytelnienia.

## Użycie

```bash
kcksefcli CheckAuthNip [opcje]
```

## Przykład

```bash
kcksefcli CheckAuthNip --active moj_profil
```

Jeśli NIP w certyfikacie to `1234567890`, a w konfiguracji `0987654321`, polecenie zwróci błąd:
`InvalidOperationException: NIP mismatch! Auth NIP: 1234567890, Config NIP: 0987654321`

---

## Konfiguracja i Uwierzytelnianie

To polecenie łączy się z serwerami KSeF i w pełni obsługuje system profili, opcje konfiguracji (`kcksefcli.yaml`) oraz automatycznej pamięci podręcznej (cache) tokenów sesyjnych.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
