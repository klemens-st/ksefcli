[Powrót do strony głównej](../README.md)

# Polecenie: `PobierzFaktury`

Pobiera wiele faktur na podstawie kryteriów wyszukiwania. Rozszerza polecenie `SzukajFaktur` o opcje zapisywania plików.

**Użycie:**
```bash
kcksefcli PobierzFaktury --from "-7days" --subjectType Subject2 -o /tmp/faktury --pdf
```

**Opcje:**
To polecenie akceptuje wszystkie opcje z `SzukajFaktur` oraz dodatkowo:

| Opcja                  | Opis                                                            | Wymagane | Domyślnie |
|------------------------|-----------------------------------------------------------------|----------|-----------|
| `-o`, `--outputdir`    | Katalog wyjściowy do zapisania faktur.                          | Tak      |           |
| `-p`, `--pdf`          | Zapisz również wersję PDF faktury.                              | Nie      |           |
| `--useInvoiceNumber`   | Użyj `InvoiceNumber` zamiast `KsefNumber` jako nazwy pliku.     | Nie      |           |
| `--no-json`            | Nie zapisuj metadanych faktury w plikach .json.                 | Nie      |           |
| `--retry-attempts`     | Liczba ponownych prób przy limicie zapytań.                     | Nie      | 5         |
| `--no-local-rate-limit`| Wyłącza lokalny limit zapytań.                                  | Nie      |           |

---

## Nazwy plików

Nazwa pliku pochodzi z odpowiedzi KSeF, a przy `--useInvoiceNumber` — z numeru nadanego przez
wystawcę faktury. Żadna z nich nie jest więc pod kontrolą tego narzędzia i przed użyciem
zostaje oczyszczona: wszystko poza literami, cyframi oraz znakami `-`, `_` i `.` zamienia się
na `_`. Polskie znaki diakrytyczne są zachowywane.

Dzięki temu numer taki jak `0004/26` zapisuje się jako `0004_26.xml` zamiast kończyć się
błędem, a plik nigdy nie powstaje poza katalogiem wskazanym opcją `--outputdir`. Jeżeli nazwa
wymagała zmiany, pojawia się ostrzeżenie z nazwą pierwotną i wynikową.

> **Znane ograniczenie: kolizje nazw.** Oczyszczanie jest wieloznaczne — różne numery mogą dać
> tę samą nazwę pliku (np. `0004/26` i `0004-26` zamieniają się w `0004_26`). Polecenie nie
> sprawdza, czy plik już istnieje, więc druga faktura **nadpisze** pierwszą bez ostrzeżenia.
> Przy pobieraniu do jednego katalogu porównaj liczbę zapisanych plików z liczbą znalezionych
> faktur; przy `--useInvoiceNumber` ryzyko jest większe, bo numery nadawane przez wystawców
> częściej zawierają znaki wymagające zamiany.

## Zakres dat a świeżo wystawione faktury

Domyślne `--dateType Issue` filtruje po dacie wystawienia zapisanej w samej fakturze (`P_1`),
a nie po dacie przyjęcia jej przez KSeF. Fakturę przesłaną przed chwilą znajdziesz przez
`--dateType Invoicing`. Szczegóły: [`SzukajFaktur`](SzukajFaktur.md).

Polecenie pobiera **wszystkie** pasujące faktury, przechodząc przez kolejne strony wyników —
`--pageSize` nie ogranicza ich liczby. Szerokie kryteria oznaczają więc nieograniczoną liczbę
plików zapisanych na dysk.


## Konfiguracja i Uwierzytelnianie

To polecenie łączy się z serwerami KSeF i w pełni obsługuje system profili, opcje konfiguracji (`kcksefcli.yaml`) oraz automatycznej pamięci podręcznej (cache) tokenów sesyjnych.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
