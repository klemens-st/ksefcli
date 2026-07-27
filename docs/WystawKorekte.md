[Powrót do strony głównej](../README.md)

# Polecenie: `WystawKorekte`

Tworzy fakturę korygującą na podstawie istniejącego pliku XML faktury z KSeF.

**Użycie:**
```bash
kcksefcli WystawKorekte <plik_wejściowy.xml> <plik_wyjściowy.xml> <pozycja1> <zmiana1> [<pozycja2> <zmiana2> ...] [opcje]
```

**Argumenty Pozycyjne:**

| Argument                    | Opis                                                                                                                                                              |
|-----------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `plik_wejściowy.xml`        | Ścieżka do pliku XML faktury, która ma być skorygowana.                                                                                                           |
| `plik_wyjściowy.xml`        | Ścieżka do zapisu nowo utworzonej faktury korygującej.                                                                                                            |
| `<pozycja>` `<zmiana>`      | Pary argumentów określające pozycje do skorygowania i ich nowe wartości. `<pozycja>` może być numerem wiersza (`NrWierszaFa`) lub nazwą towaru/usługi (`P_7`). `<zmiana>` może być nową ilością (np. `5`) lub różnicą (np. `+2`, `-1`). |

**Opcje:**

| Opcja                  | Opis                                                   | Wymagane | Domyślnie    |
|------------------------|--------------------------------------------------------|----------|--------------|
| `--PrzyczynaKorekty`   | Powód korekty (element `PrzyczynaKorekty`).            | Nie      | Pusty ciąg znaków |
| `--TypKorekty`         | Typ korekty (element `TypKorekty`).                    | Nie      | `null`       |
| `--no-validate`        | Pomiń walidację XML po utworzeniu korekty.             | Nie      |              |

**Przykład:**

Koryguje ilość dla pozycji o numerze 1 na 5 sztuk w pliku `faktura.xml` i zapisuje wynik jako `korekta.xml`.

```bash
kcksefcli WystawKorekte faktura.xml korekta.xml 1 5 --PrzyczynaKorekty "Błędna ilość"
```

---

## Jak korekta przedstawia zmienioną pozycję

Skorygowana pozycja nie jest w wynikowym pliku podmieniana w miejscu. Zamiast tego trafiają tam
**dwa wiersze**: kopia pierwotnej pozycji z odwróconym znakiem (wartości ujemne) oraz pozycja
z nową, poprawną wartością. Dzięki temu suma dla tej stawki VAT wyraża samą **różnicę**
względem faktury pierwotnej, a pozycje nietknięte korektą zachowują pełne wartości.

Wynik jednej korekty ma więc więcej wierszy niż faktura wejściowa. Jest to celowa semantyka
tego polecenia, utrwalona w teście porównującym bajty (`tests/expected_korekta.xml`).
