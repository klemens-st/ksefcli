[Powrót do strony głównej](../README.md)

# Polecenie: `UniewaznijCertyfikat`

Unieważnia certyfikat KSeF.

**Użycie:**
```bash
kcksefcli UniewaznijCertyfikat <numer-seryjny>
```

**Argumenty:**

| Argument        | Opis                                      | Wymagane |
|-----------------|-------------------------------------------|----------|
| `numer-seryjny` | Numer seryjny certyfikatu.                | Tak      |

**Opcje:**

| Opcja      | Opis                                                                                                          | Domyślnie |
|------------|---------------------------------------------------------------------------------------------------------------|-----------|
| `--reason` | Powód unieważnienia: `KeyCompromise`, `AffiliationChanged`, `Superseded`, `CessationOfOperation`, `Other`.    | `Other`   |
| `--yes`    | Potwierdza nieodwracalną operację w środowisku produkcyjnym bez pytania.                                       |           |

---

## Unieważnienie na produkcji wymaga potwierdzenia

Unieważnienia certyfikatu nie da się cofnąć, dlatego w środowisku produkcyjnym polecenie
najpierw prosi o potwierdzenie. Bez terminala — w skrypcie, w CI i w sesji agenta — **brak
`--yes` oznacza odmowę** i zakończenie z kodem `1`. Środowisko o nierozpoznanej nazwie jest
traktowane jak produkcyjne. Pełny opis zasad:
[**Bezpieczeństwo w pracy z agentami**](BezpieczenstwoAgentow.md).

---


## Konfiguracja i Uwierzytelnianie

To polecenie łączy się z serwerami KSeF i w pełni obsługuje system profili, opcje konfiguracji (`kcksefcli.yaml`) oraz automatycznej pamięci podręcznej (cache) tokenów sesyjnych.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
