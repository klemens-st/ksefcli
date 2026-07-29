# Bezpieczeństwo pracy agentowej

Dokument opisuje zasady uruchamiania `kcksefcli` przez agenta (np. Claude Code) albo w innym
trybie bezobsługowym. Wcześniejsze poprawki bezpieczeństwa zmniejszały **prawdopodobieństwo**
pomyłki. Ten dokument ogranicza jej **zasięg**.

## Zasada podstawowa

Agent domyślnie pracuje w środowisku nieprodukcyjnym. Dostęp do produkcji jest świadomą,
jednorazową decyzją człowieka — nie stanem domyślnym konfiguracji.

## 1. Domyślnie środowisko `test` albo `demo`

Ustaw `environment: test` (lub `demo`) w profilu, z którego korzysta agent. Od tego pola zależą
dwa zabezpieczenia:

| Pole / mechanizm | `test` | `demo` | `prod` i wszystko inne |
|---|---|---|---|
| Weryfikacja łańcucha certyfikatu (`verify_certificate_chain`) | wyłączona | włączona | włączona |
| Bramka potwierdzenia operacji nieodwracalnych | brak | brak | **wymagana** |

Nierozpoznana nazwa środowiska (literówka, pusta wartość) jest traktowana jak **produkcja**.
Błąd w profilu ma zawodzić w stronę bezpieczną, a nie po cichu wyłączać zabezpieczenia.

## 2. Poświadczenia produkcyjne poza domyślną ścieżką

Nie trzymaj profilu produkcyjnego w pliku, który `kcksefcli` znajdzie sam. Kolejność
wyszukiwania to `--config`, potem zmienna `KCKSEFCLI_CONFIG`, a na końcu domyślna lokalizacja:
`$XDG_CONFIG_HOME/kcksefcli/kcksefcli.yaml` lub `~/.config/kcksefcli/kcksefcli.yaml` na Linuksie
(`%LOCALAPPDATA%\kcksefcli\` na Windows, `~/Library/Application Support/kcksefcli/` na macOS).
Profil wybiera `--active` albo zmienna `KCKSEFCLI_ACTIVE`.

Zalecenie: konfiguracja agenta w domyślnej lokalizacji zawiera **wyłącznie** profile testowe, a
produkcja mieszka w osobnym pliku podawanym jawnie przez `--config`. Wtedy uruchomienie
produkcyjne widać w linii poleceń.

`.gitignore` pokrywa już `secrets`, `secret`, `*.ksefcli.yaml` i `.env`.

## 3. Bramka na operacjach nieodwracalnych

Trzy polecenia robią coś, czego kolejne polecenie nie cofnie:

| Polecenie | Skutek |
|---|---|
| `PrzeslijFaktury` | wysyła faktury do KSeF — czynność prawna |
| `UniewaznijCertyfikat` | unieważnia certyfikat |
| `NowyCertyfikat` | zużywa limitowaną pulę wystawień |

W środowisku produkcyjnym każde z nich przechodzi przez bramkę:

| Sytuacja | Zachowanie |
|---|---|
| środowisko `test`/`demo` | wykonuje się bez pytania |
| produkcja + terminal | pyta `[t/N]`; samo Enter oznacza **nie** |
| produkcja + `--yes` | wykonuje się |
| **produkcja + brak terminala + brak `--yes`** | **odmowa, kod wyjścia 1** |

Ostatni wiersz jest właściwym zabezpieczeniem. Agent nie ma terminala, więc nie odpowie na
pytanie; gdyby „brak terminala" oznaczał zgodę, bramka byłaby tylko dekoracją.

**Nie dodawaj `--yes` na stałe do poleceń agenta.** Flaga ma być decyzją operatora przy
konkretnym uruchomieniu. Logika i uzasadnienie: `src/KCKSeFCli/Utils/DangerousOperation.cs`.

## 4. Kontrakt ponawiania — kod wyjścia 2

| Kod | Znaczenie | Czy ponawiać? |
|---|---|---|
| 0 | sukces | nie ma czego |
| 1 | niepowodzenie — nic nie zostało przyjęte | tak, bezpiecznie |
| **2** | **częściowy sukces — wynik niepełny** | **NIE**, patrz niżej |
| 3 | nieobsłużony wyjątek — stan nieznany | **NIE** bez sprawdzenia |

Kod `2` znaczy co innego przy zapisie i przy odczycie, więc sprawdź, które polecenie go zwróciło:

- **`PrzeslijFaktury`** — KSeF przyjął część paczki. Ślepe ponowienie **zduplikuje** faktury już
  złożone. Postępowanie niżej.
- **`SzukajFaktur` i `PobierzFaktury`** — zapytanie trafiło w limit 10 000 wyników i zostało
  obcięte. Nic się nie zepsuło, ale wynik **nie jest kompletem** pasujących faktur, a ani JSON na
  standardowym wyjściu, ani katalog wyjściowy tego po sobie nie pokazują. Ponowienie tego samego
  zapytania da ten sam obcięty wynik — trzeba zawęzić kryteria, najczęściej dzieląc zakres dat na
  krótsze przedziały. Nie traktuj takiego wyniku jako pełnej listy, np. przy uzgadnianiu
  otrzymanych faktur.

Postępowanie po kodzie `2` z `PrzeslijFaktury` lub po kodzie `3`:

1. Zapisz `ReferenceNumber` sesji — jest w logu i tylko on pozwala ustalić stan.
2. Sprawdź, co faktycznie przeszło (`PrzeslijFaktury` wypisuje status każdej faktury; UPO
   pobierzesz przez `--upodir`).
3. Ponów **wyłącznie** faktury odrzucone, jako nową paczkę.

To samo dotyczy przekroczenia czasu oczekiwania na status sesji: polecenie kończy się wtedy
kodem `1`, ale wypisuje, że stan faktur jest nieznany. Ten komunikat ma pierwszeństwo przed
kodem wyjścia — sprawdź sesję, zanim ponowisz.

## 5. Limity zapytań

`SzukajFaktur`, `PobierzFaktury` i `PrzeslijFaktury` mają lokalne ograniczanie zapytań i
ponawiają je po HTTP 429. Ponowienie następuje **tylko** po 429, czyli po odrzuceniu żądania,
zanim KSeF cokolwiek nim zrobił — dlatego nie może zdublować operacji.

Nie używaj `--no-local-rate-limit` w pracy wsadowej. `--retry-attempts` domyślnie wynosi 5.

## 6. Czego bramka nie obejmuje

Świadomie, żeby nie sugerować ochrony, której nie ma:

- Polecenia odczytujące (`SzukajFaktur`, `PobierzFaktury`, `PokazLimity`, …) nie są bramkowane.
  Mogą jednak zużywać limity API i zapisywać pliki na dysk.
- `WystawFaktureOffline` przygotowuje fakturę lokalnie; wysyła ją dopiero `PrzeslijFaktury`.
- Polecenia operujące na plikach XML (`NowaFaktura`, `WystawKorekte`, `DodajPozycjeNaFakturze`)
  nie kontaktują się z KSeF, ale **nadpisują pliki wyjściowe** bez pytania.
- Bramka nie sprawdza *treści* faktur. Poprawność kwot i korekt to nadal odpowiedzialność
  wywołującego; `WeryfikujXML` sprawdza tylko zgodność ze schematem XSD.
