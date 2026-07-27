[Powrót do strony głównej](../README.md)

# Polecenie: `SzukajFaktur`

Wyszukuje faktury na podstawie podanych kryteriów. Odpowiada endpointowi `GET /online/Query/Invoice/Sync`.

**Użycie:**
```bash
kcksefcli SzukajFaktur --from "-7days" --subjectType Subject2
```

**Opcje:**

| Opcja                                   | Opis                                                                                                                                     | Domyślnie    | Wymagane |
|-----------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------|--------------|----------|
| `-s`, `--subjectType`                   | Typ podmiotu dla kryteriów filtrowania. Możliwe wartości: `Subject1` (albo `1`, `sprzedawca`), `Subject2` (albo `2`, `nabywca`), `Subject3` (albo `3`), `SubjectAuthorized` (albo `4`). | `Subject1`   | Nie      |
| `--from`                                | Data początkowa. Szczegóły formatu daty: zobacz [ParseDate](ParseDate.md).                                       |              | Tak      |
| `--to`                                  | Data końcowa. Szczegóły formatu daty: zobacz [ParseDate](ParseDate.md).                                                   |              | Nie      |
| `--dateType`                            | Typ daty używany w zakresie dat. Możliwe wartości: `Issue`, `Invoicing`, `PermanentStorage`. Zobacz uwagę niżej.                          | `Issue`      | Nie      |
| `--pageOffset`                          | Numer pozycji, od której zaczyna się pobieranie wyników. Nie ogranicza ich liczby — zobacz uwagę o paginacji.                             | `0`          | Nie      |
| `--pageSize`                            | Liczba wyników pobieranych w jednym zapytaniu. Nie ogranicza łącznej liczby zwróconych faktur — zobacz uwagę o paginacji.                 | `10`         | Nie      |
| `--retry-attempts`                      | Liczba ponownych prób po przekroczeniu limitu zapytań (HTTP 429).                                                                         | `5`          | Nie      |
| `--no-local-rate-limit`                 | Wyłącza lokalne ograniczanie liczby zapytań do API.                                                                                      |              | Nie      |
| `--restrictToPermanentStorageHwmDate`   | Ogranicza filtrowanie do `PermanentStorageHwmDate`. Dotyczy tylko `dateType` = `PermanentStorage`.                                     |              | Nie      |
| `--ksefNumber`                          | Numer KSeF faktury (dokładne dopasowanie).                                                                                               |              | Nie      |
| `--invoiceNumber`                       | Numer faktury nadany przez wystawcę (dokładne dopasowanie).                                                                              |              | Nie      |
| `--amountType`                          | Typ filtru kwotowego. Możliwe wartości: `Brutto`, `Netto`, `Vat`.                                                                          |              | Nie      |
| `--amountFrom`                          | Minimalna wartość kwoty.                                                                                                                 |              | Nie      |
| `--amountTo`                            | Maksymalna wartość kwoty.                                                                                                                |              | Nie      |
| `--sellerNip`                           | NIP sprzedawcy (dokładne dopasowanie).                                                                                                   |              | Nie      |
| `--buyerIdentifierType`                 | Typ identyfikatora nabywcy. Możliwe wartości: `Nip`, `VatUe`, `Other`, `None`.                                                            |              | Nie      |
| `--buyerIdValue`                        | Wartość identyfikatora nabywcy (dokładne dopasowanie).                                                                                   |              | Nie      |
| `--currencyCodes`                       | Kody walut, oddzielone przecinkami (np. `PLN,EUR`).                                                                                       |              | Nie      |
| `--invoicingMode`                       | Tryb fakturowania: `Online` lub `Offline`.                                                                                               |              | Nie      |
| `--isSelfInvoicing`                     | Czy faktura jest samofakturowaniem.                                                                                                      |              | Nie      |
| `--formType`                            | Typ dokumentu. Możliwe wartości: `FA`, `PEF`, `RR`.                                                                                      |              | Nie      |
| `--invoiceTypes`                        | Typy faktur, oddzielone przecinkami (np. `Vat`, `Zal`, `Kor`).                                                                             |              | Nie      |
| `--hasAttachment`                       | Czy faktura posiada załącznik.                                                                                                           |              | Nie      |

---

## Szukanie dopiero co wystawionej faktury: `--dateType`

Domyślne `Issue` filtruje po **dacie wystawienia zapisanej w samej fakturze** (pole `P_1`), a nie
po dacie jej przyjęcia przez KSeF. Faktura wystawiona z datą przyszłą albo przeszłą nie znajdzie
się więc w oknie czasowym wokół „teraz”, choćby została przesłana przed chwilą.

Do odnalezienia faktury, którą właśnie przesłano, służy `--dateType Invoicing` — to data
przyjęcia dokumentu przez KSeF:

```bash
kcksefcli SzukajFaktur --from "-10minutes" --dateType Invoicing
```

Wyszukiwanie, które z tego powodu nic nie zwraca, wygląda dokładnie tak samo jak wyszukiwanie
bez uprawnień do danej faktury — w obu przypadkach wynikiem jest pusta lista.

## Paginacja: polecenie pobiera wszystkie wyniki

`--pageSize` i `--pageOffset` sterują pojedynczym zapytaniem do API, a nie wielkością wyniku.
Polecenie samo przechodzi przez kolejne strony, dopóki API zgłasza, że są następne, i zwraca
**wszystkie** pasujące faktury. `--pageSize` decyduje więc tylko o tym, na ile zapytań zostanie
podzielone pobieranie.

Ma to znaczenie przede wszystkim dla [`PobierzFaktury`](PobierzFaktury.md), które zapisuje każdy
wynik na dysk: szerokie kryteria oznaczają nieograniczoną liczbę plików, a nie jedną stronę.

---


## Konfiguracja i Uwierzytelnianie

To polecenie łączy się z serwerami KSeF i w pełni obsługuje system profili, opcje konfiguracji (`kcksefcli.yaml`) oraz automatycznej pamięci podręcznej (cache) tokenów sesyjnych.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
