[Powrót do strony głównej](../README.md)

# Polecenie: `NowaFaktura`

Generuje nową, prostą fakturę XML zgodną ze standardem KSeF na podstawie wejściowego pliku specyfikacji zapisanego w formacie YAML. Narzędzie automatycznie mapuje przyjazną składnię YAML na skomplikowane drzewo XML oczekiwane przez system e-Faktur. Automatycznie też weryfikuje wygenerowaną strukturę względem schemy (o ile walidacja nie zostanie wyłączona opcją).

**Użycie:**
```bash
kcksefcli NowaFaktura <plik-yaml> <plik-wyjsciowy-xml>
```

**Argumenty:**

| Argument               | Opis                                                 | Wymagane |
|------------------------|------------------------------------------------------|----------|
| `plik-yaml`            | Ścieżka do wejściowego pliku w formacie YAML.        | Tak      |
| `plik-wyjsciowy-xml`   | Ścieżka do utworzonego na dysku pliku faktury w XML. | Tak      |

**Opcje:**

| Opcja               | Opis                                             | Domyślnie |
|---------------------|--------------------------------------------------|-----------|
| `--bez-walidacji`   | Pomija walidację XML po utworzeniu pliku.        | False     |

## Format pliku YAML dla NowaFaktura

Polecenie `NowaFaktura` przyjmuje jako argument plik w formacie YAML definiujący fakturę. 

**Uwaga do sekcji Nabywcy (Kupujący):** Jeśli dla kupującego nie zostanie podany `Nip` oraz `NrID`, system uzna, że dokument jest wystawiany dla **osoby fizycznej** nieprowadzącej działalności gospodarczej, przypisując w wygenerowanym dokumencie element `<BrakID>1</BrakID>` zgodnie z wymaganiami KSeF.

Przykład struktury pliku YAML:

```yaml
Sprzedawca:
  Nip: "5260202588"
  Nazwa: "Firma Sprzedawca" # Opcjonalnie, zostanie pobrane automatycznie z rejestru NIP jeśli puste
  Adres: "ul. Prosta 1, 00-001 Warszawa" # Opcjonalnie, pobierane jw.
Kupujący:
  Nip: "5223217667" # Może być Nip lub NrID lub całkowity brak
  NrID: "1234567890" # Alternatywa dla Nip
  Nazwa: "Klient Kupujący"
DataWykonania: "2026-02-15" # Opcjonalnie, mapowane na P_6 (domyślnie data dzisiejsza)
DodatkowyOpis: # Opcjonalna sekcja dla dodatkowych opisów faktury
  - Klucz: "Klucz1"
    Wartosc: "Wartosc1"
Pozycje:
  - Nazwa: "Usługa IT"
    Jednostka: "godz" # Domyślnie "" (jeśli puste, P_8A nie pojawi się w XML)
    Ilosc: 1 # Opcjonalnie (jeśli puste, P_8B nie pojawi się w XML)
    StawkaPodatku: "23" # Opcjonalnie, domyślnie "23" (użyj "odwrotne obciążenie" lub "oo" dla oo)
    WartoscBrutto: 1230.00
```

---

## Ograniczenie: obsługiwane są tylko stawki z parą pól `P_13_x`/`P_14_x`

> **Nie używaj tego polecenia do faktur ze stawką `0%`, `zw`, `np` ani `oo`.**

Poprawnie obsługiwane są wyłącznie stawki mające parę pól netto/VAT: `23`, `22`, `8`, `7`, `5`
i `4`. Dla pozostałych polecenie dolicza wartość pozycji do sumy `P_15`, ale **nie wypisuje
żadnego pola `P_13_x`**. Sprzedaż jest wtedy wykazana w sumie ogólnej i w żadnym polu
szczegółowym, co daje fakturę, która się nie sumuje.

Polecenie ostrzega o tym na wyjściu, ale **walidacja XSD tego nie wykryje** — schemat nie
sprawdza, czy sumy się zgadzają. Przejście `WeryfikujXML` nie jest więc dowodem, że kwoty są
poprawne.

Dotyczy to również wartości `oo` i `odwrotne obciążenie` wymienionych w przykładzie wyżej:
poza brakiem pola `P_13_x` odwrotne obciążenie wymaga także adnotacji `P_18` w sekcji
`Adnotacje`, której polecenie nie uzupełnia.

Do dodania pozycji ze stawką zerową (`0 KR`, `0 WDT`, `0 EX`) do istniejącej faktury służy
[`DodajPozycjeNaFakturze`](DodajPozycjeNaFakturze.md), które obsługuje je poprawnie. Pozycje
`zw`, `np` i `oo` trzeba na razie uzupełnić ręcznie.
