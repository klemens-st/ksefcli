[Powrót do strony głównej](../README.md)

# Polecenie: `NowyCertyfikat`

Składa wniosek o nowy certyfikat KSeF, opcjonalnie zapisując plik CSR (żądanie certyfikatu),
wystawiony certyfikat oraz odpowiadający mu klucz prywatny (zakodowane w Base64).

Zarówno CSR, jak i klucz prywatny powstają **lokalnie**, na tej maszynie. Wywołanie API służy
wyłącznie pobraniu parametrów wniosku; klucz prywatny nigdy nie opuszcza komputera, a do KSeF
wysyłany jest sam CSR.

**Użycie:**
```bash
kcksefcli NowyCertyfikat --certificateName "NowyCert2026"
```

**Opcje specyficzne:**

| Opcja                     | Opis                                                                                                                      | Domyślnie        |
|---------------------------|---------------------------------------------------------------------------------------------------------------------------|------------------|
| `--certificateName`       | Wymagane. Nazwa dla wydawanego certyfikatu.                                                                               |                  |
| `--certificateType`       | Typ certyfikatu (`Authentication` lub `Offline`).                                                                         | `Authentication` |
| `--csrOutputPath`         | Ścieżka pliku wyjściowego, w którym zostanie zapisany wygenerowany lokalnie CSR (zakodowany w Base64).                    |                  |
| `--privateKeyOutputPath`  | Ścieżka pliku wyjściowego, w którym zostanie zapisany klucz prywatny wygenerowany lokalnie przez narzędzie (w Base64).    |                  |
| `--certificateOutputPath` | Ścieżka pliku wyjściowego dla wystawionego i uzyskanego certyfikatu (zakodowanego w Base64).                              |                  |
| `--validFrom`             | Data początkowa ważności certyfikatu (np. `2026-01-01`). Jeśli nie podano, używany jest bieżący czas UTC tej maszyny.    | Teraz (UTC)      |
| `--yes`                   | Potwierdza nieodwracalną operację w środowisku produkcyjnym bez pytania.                                                  |                  |

*Polecenie obsługuje również wszystkie ogólne opcje konfiguracyjne np. do podawania poświadczeń.*

---

## Wystawienie certyfikatu na produkcji wymaga potwierdzenia

Wystawienie certyfikatu zużywa limit i jest nieodwracalne, dlatego w środowisku produkcyjnym
polecenie najpierw prosi o potwierdzenie. Bez terminala — w skrypcie, w CI i w sesji agenta —
**brak `--yes` oznacza odmowę** i zakończenie z kodem `1`. Środowisko o nierozpoznanej nazwie
jest traktowane jak produkcyjne. Pełny opis zasad:
[**Bezpieczeństwo w pracy z agentami**](BezpieczenstwoAgentow.md).


## Konfiguracja i Uwierzytelnianie

To polecenie łączy się z serwerami KSeF i w pełni obsługuje system profili, opcje konfiguracji (`kcksefcli.yaml`) oraz automatycznej pamięci podręcznej (cache) tokenów sesyjnych.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
