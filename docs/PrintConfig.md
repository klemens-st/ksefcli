[Powrót do strony głównej](../README.md)

# Polecenie: `PrintConfig`

Wypisuje aktywną konfigurację w formacie YAML (domyślnie) lub JSON (z opcją `--json`).

**Użycie:**
```bash
kcksefcli PrintConfig [--json] [--reveal]
```

**Opcje:**

| Opcja       | Opis                                | Domyślnie |
|-------------|-------------------------------------|-----------|
| `--json`    | Wypisuje konfigurację w formacie JSON. | `false`   |
| `--reveal`  | Wypisuje sekrety jawnym tekstem.    | `false`   |

---

## Sekrety są domyślnie ukrywane

Konfiguracja jest rozwiązywana (*resolved*) zanim to polecenie ją zobaczy: `private_key_file`
zostaje wczytany do `private_key`, a `password_env`, `password_file` i `password_cmd` — do
`password`. Wskazanie pliku lub menedżera haseł nie chroni więc samo z siebie sekretu przed
wypisaniem; zmienia tylko jego źródło.

Dlatego `private_key`, `password`, `certificate` oraz `token` są zastępowane napisem
`<redacted>`. Pola `*_file`, `*_env` i `*_cmd` pozostają widoczne — mówią, skąd pochodzi
sekret, nie ujawniając go, i to one odpowiadają na pytanie „której konfiguracji faktycznie
używam”. Pole nieustawione pozostaje puste, a nie `<redacted>`, żeby nie sugerować, że coś
jest skonfigurowane.

`--reveal` wypisuje wartości jawnym tekstem i emituje ostrzeżenie na standardowe wyjście
błędów. Używaj go świadomie: w przepływach agentowych wynik polecenia trafia do kontekstu
modelu i do zapisu sesji.


## Konfiguracja i Uwierzytelnianie

To polecenie łączy się z serwerami KSeF i w pełni obsługuje system profili, opcje konfiguracji (`kcksefcli.yaml`) oraz automatycznej pamięci podręcznej (cache) tokenów sesyjnych.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
