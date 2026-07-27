[Powrót do strony głównej](../README.md)

# Polecenie: `DodajPozycjeNaFakturze`

Służy do dodawania nowej pozycji (towaru lub usługi) do istniejącej faktury KSeF w formacie XML.

Polecenie parsowania faktury w formacie XML, po czym wstrzykuje nową pozycję do sekcji `FaWiersz`, ponownie wyliczając sumy podatków oraz wartości brutto i podmieniając je w pliku. Waliduje też zaktualizowaną fakturę na zgodność ze schematem XML.

**Użycie:**
```bash
kcksefcli DodajPozycjeNaFakturze <plik-wejsciowy-xml> [<plik-wyjsciowy-xml>] --nazwa "Usługa" --miara "szt" --ilosc 1 --cena-netto 100 --stawka-vat 23

# stawka zerowa — pełna postać, nie samo "0"
kcksefcli DodajPozycjeNaFakturze faktura.xml --nazwa "Eksport" --miara "szt" --ilosc 1 --cena-netto 100 --stawka-vat "0 EX"
```

**Argumenty:**

| Argument             | Opis                                                                                                    | Wymagane |
|----------------------|---------------------------------------------------------------------------------------------------------|----------|
| `plik-wejsciowy-xml` | Ścieżka do istniejącego pliku XML z fakturą KSeF.                                                       | Tak      |
| `plik-wyjsciowy-xml` | Ścieżka wyjściowa dla pliku XML. Jeśli nie zostanie podany, plik wejściowy zostanie nadpisany.          | Nie      |

**Opcje:**

| Opcja                 | Opis                                             | Wymagane |
|-----------------------|--------------------------------------------------|----------|
| `--nazwa`             | Nazwa towaru lub usługi (pole P_7).              | Tak      |
| `--miara`             | Jednostka miary (pole P_8A).                     | Tak      |
| `--ilosc`             | Ilość (pole P_8B).                               | Tak      |
| `--cena-netto`        | Cena jednostkowa netto (pole P_9A).              | Tak      |
| `--stawka-vat`        | Stawka podatku VAT (pole P_12). Obsługiwane: `23`, `22`, `8`, `7`, `5`, `4` oraz stawki zerowe `0 KR`, `0 WDT` i `0 EX`. | Tak      |
| `--bez-walidacji`     | Pomija walidację XML po modyfikacji pliku.       | Nie      |

---

## Obsługiwane stawki VAT

Samo `0` nie jest przyjmowane — schemat `TStawkaPodatku` nie zna takiej wartości. Stawka zerowa
zawsze niesie ze sobą rodzaj transakcji i trzeba ją podać w pełnej postaci: `0 KR` (krajowa),
`0 WDT` (wewnątrzwspólnotowa dostawa towarów) lub `0 EX` (eksport). Każda z nich trafia do
odpowiadającego jej pola `P_13_6_1`, `P_13_6_2` albo `P_13_6_3`.

Pozycje `zw`, `oo`, `np I` i `np II` nie są przez to polecenie obsługiwane — każda z nich
wymaga dodatkowo adnotacji w sekcji `Adnotacje` (np. `P_18` dla `oo`), których polecenie nie
uzupełnia. Takie pozycje trzeba dodać ręcznie.

Polecenie odmawia również wtedy, gdy faktura nie zawiera pól sumujących wymaganych dla podanej
stawki. Dodanie pozycji rozjechałoby wówczas sumy na fakturze, dlatego zamiast zapisać
niespójny dokument polecenie kończy się błędem i wypisuje nazwy brakujących pól.
