[Powrót do strony głównej](../README.md)

# Polecenie: `PrzeslijFaktury`

Wysyła faktury w formacie XML do KSeF.

**Użycie:**
```bash
kcksefcli PrzeslijFaktury faktura1.xml faktura2.xml --upodir /tmp/upo --upopdf
```

**Argumenty:**

| Argument      | Opis                                  | Wymagane |
|---------------|---------------------------------------|----------|
| `pliki`       | Ścieżki do plików XML z fakturami.    | Tak      |

**Opcje:**

> **Zalecenie:** Zawsze zaleca się podawanie flag `--upodir` oraz `--upopdf` przy wysyłaniu faktur. Dzięki temu narzędzie natychmiast zapisze Urzędowe Poświadczenie Odbioru (UPO) wraz z czytelnym wygenerowanym dokumentem PDF potwierdzającym poprawność wysyłki i nadanie numeru KSeF.

| Opcja              | Opis                                                | Wymagane |
|--------------------|-----------------------------------------------------|----------|
| `-u`, `--upodir`   | Katalog do zapisu plików UPO.                       | Nie      |
| `--upopdf`         | Konwertuje UPO od razu na format PDF.               | Nie      |
| `--uposesji`       | Zapisuje UPO sesji (zbiorcze UPO).                  | Nie      |
| `--offlinemode`    | Ustawia tryb offline dla sesji.                     | Nie      |
| `--retry-attempts` | Liczba ponownych prób po przekroczeniu limitu zapytań (HTTP 429). Domyślnie `5`. | Nie |
| `--no-local-rate-limit` | Wyłącza lokalne ograniczanie liczby zapytań do API. | Nie      |
| `--yes`            | Potwierdza nieodwracalną operację w środowisku produkcyjnym bez pytania. | Nie |

---

## Wysyłka na produkcję wymaga potwierdzenia

Przesłanie faktury do KSeF jest nieodwracalne, dlatego w środowisku produkcyjnym polecenie
najpierw prosi o potwierdzenie. Bez terminala — a więc w skrypcie, w CI i w sesji agenta —
**brak `--yes` oznacza odmowę** i zakończenie z kodem `1`, zanim cokolwiek zostanie wysłane.
Środowisko o nierozpoznanej nazwie jest traktowane jak produkcyjne.

Poza produkcją (np. `test`, `demo`) potwierdzenie nie jest wymagane. Pełny opis zasad:
[**Bezpieczeństwo w pracy z agentami**](BezpieczenstwoAgentow.md).

## Kody wyjścia

| Kod | Znaczenie                                                                                       |
|-----|-------------------------------------------------------------------------------------------------|
| `0` | Wszystkie faktury zostały przyjęte.                                                             |
| `1` | Żadna faktura nie została przyjęta, odmówiono potwierdzenia albo sesja nie została zamknięta.   |
| `2` | Część faktur została przyjęta, a część nie.                                                     |
| `3` | Nieobsłużony wyjątek.                                                                            |

Kod `2` jest wydzielony celowo: skoro część faktur już trafiła do KSeF, ponowne uruchomienie
tego samego polecenia zduplikowałoby je. Po kodzie `2` należy sprawdzić UPO i wysłać ponownie
tylko te faktury, które nie zostały przyjęte.

---


## Konfiguracja i Uwierzytelnianie

To polecenie łączy się z serwerami KSeF i w pełni obsługuje system profili, opcje konfiguracji (`kcksefcli.yaml`) oraz automatycznej pamięci podręcznej (cache) tokenów sesyjnych.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
