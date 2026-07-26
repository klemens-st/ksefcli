# kcksefcli

`kcksefcli` to narzędzie wiersza poleceń (CLI) dla systemu Linux, napisane w języku C#, które ułatwia interakcję z Krajowym Systemem e-Faktur (KSeF) w Polsce. Aplikacja wykorzystuje bibliotekę kliencką `ksef-client-csharp` do komunikacji z usługami KSeF.

## Spis Treści

- [Instalacja](#instalacja)
- [Przykłady użycia](#przykłady-użycia)
- [Konfiguracja](#konfiguracja)
  - [Struktura pliku `kcksefcli.yaml`](#struktura-pliku-kcksefcliyaml)
  - [Opcje Konfiguracyjne](#opcje-konfiguracyjne)
  - [Przykład Konfiguracji](#przykład-konfiguracji)
- [Użycie](#użycie)
  - [Opcje Globalne](#opcje-globalne)
  - [Dostępne Polecenia](#dostępne-polecenia)
- [Polecenia](#polecenia)
  - [`TestAuth`](docs/TestAuth.md)
  - [`TestCertAuth`](docs/TestCertAuth.md)
  - [`CheckAuthNip`](docs/CheckAuthNip.md)
  - [`DodajPozycjeNaFakturze`](docs/DodajPozycjeNaFakturze.md)
  - [`GetFaktura`](docs/GetFaktura.md)
  - [`LinkDoFaktury`](docs/LinkDoFaktury.md)
  - [`LinkWeryfikacjiFaktury`](docs/LinkWeryfikacjiFaktury.md)
  - [`NowyCertyfikat`](docs/NowyCertyfikat.md)
  - [`NowaFaktura`](docs/NowaFaktura.md)
  - [`ParseDate`](docs/ParseDate.md)
  - [`PobierzCertyfikat`](docs/PobierzCertyfikat.md)
  - [`PobierzInfoONip`](docs/PobierzInfoONip.md)
  - [`PobierzFaktury`](docs/PobierzFaktury.md)
  - [`PokazLimity`](docs/PokazLimity.md)
  - [`PrintConfig`](docs/PrintConfig.md)
  - [`PrzeslijFaktury`](docs/PrzeslijFaktury.md)
  - [`QRDoFaktury`](docs/QRDoFaktury.md)
  - [`QRWeryfikacjiFaktury`](docs/QRWeryfikacjiFaktury.md)
  - [`SprawdzLimitCertyfikatow`](docs/SprawdzLimitCertyfikatow.md)
  - [`SzukajFaktur`](docs/SzukajFaktur.md)
  - [`TestTokenAuth`](docs/TestTokenAuth.md)
  - [`TokenRefresh`](docs/TokenRefresh.md)
  - [`UniewaznijCertyfikat`](docs/UniewaznijCertyfikat.md)
  - [`WeryfikujXML`](docs/WeryfikujXML.md)
  - [`WylistujCertyfikaty`](docs/WylistujCertyfikaty.md)
  - [`WystawFaktureOffline`](docs/WystawFaktureOffline.md)
  - [`WystawPodobnaFakture`](docs/WystawPodobnaFakture.md)
  - [`WystawKorekte`](docs/WystawKorekte.md)
  - [`XMLExtract`](docs/XMLExtract.md)
  - [`XMLRemoveNamespace`](docs/XMLRemoveNamespace.md)
  - [`XML2PDF`](docs/XML2PDF.md)
- [Rozwój](#rozwój)
  - [Wymagania](#wymagania)
  - [Przygotowanie środowiska](#przygotowanie-środowiska)
  - [Testy](#testy)
  - [Praca z Claude Code CLI](#praca-z-claude-code-cli)
- [Uwierzytelnianie w KSeF](#uwierzytelnianie-w-ksef)
- [Autor i Licencja](#autor-i-licencja)

## Instalacja

Możesz pobrać statycznie linkowaną binarkę `kcksefcli` bezpośrednio z artefaktów GitLab CI/CD, a następnie umieścić ją w katalogu znajdującym się w `PATH` (np. `~/.local/bin`).

Poniższy link jest przeznaczony dla systemu Linux.

```bash
mkdir -p ~/.local/bin
curl -LsS https://gitlab.com/kamcuk/kcksefcli/builds/artifacts/main/download?job=linux_build_main | zcat > ~/.local/bin/kcksefcli
chmod +x ~/.local/bin/kcksefcli
export PATH="$HOME/.local/bin:$PATH"
```

### Bezpośrednie linki do pobrania

- [Linux x64](https://gitlab.com/kamcuk/kcksefcli/-/jobs/artifacts/main/raw/kcksefcli?job=linux_build_main)
- [Windows x64](https://gitlab.com/kamcuk/kcksefcli/-/jobs/artifacts/main/raw/kcksefcli.exe?job=windows_build_main)
- [Windows x86 (.NET 6.0)](https://gitlab.com/kamcuk/kcksefcli/-/jobs/artifacts/main/raw/kcksefcli.exe?job=win-x86-net6.0_build_main)

### Aktualizacja

Aktualizacja polega na ponownym pobraniu binarki w sposób opisany powyżej.

Nie ma polecenia `SelfUpdate`. Podmieniało ono działającą binarkę plikiem pobranym spod
dowolnego adresu URL, bez podpisu i bez sumy kontrolnej, domyślnie z tocząco budowanego
artefaktu CI z gałęzi `main`. Nie ma tu nic stabilnego, co dałoby się przypiąć sumą SHA-256,
więc nie da się tego bezpiecznie utwardzić. Narzędzie ma dostęp do danych uwierzytelniających
do KSeF, dlatego decyzja o podmianie jego binarki należy do procesu wdrożeniowego, a nie do
samego narzędzia.


## Przykłady użycia

Wyszukiwanie numeru KSeF dla faktury o konkretnym numerze:
```bash
$ kcksefcli SzukajFaktur -q -c kcksefcli.yaml --from "-1week" --to "now" --invoiceNumber '0004/26' | jq -r '.Invoices[0].KsefNumber'
12312312312-20260117-XXXXXXXXXXXX-5C
```

Pobieranie wszystkich faktur zakupowych z ostatniego miesiąca do wskazanego katalogu w formacie XML i PDF:
```bash
$ kcksefcli PobierzFaktury --from "-1month" --subjectType Subject2 --outputdir ./faktury_zakupowe --pdf
```

Przesyłanie faktury z użyciem konkretnego profilu:
```bash
$ kcksefcli PrzeslijFaktury -c kcksefcli.yaml -f d03900-001.xml  -a firma2
```

Wyszukiwanie faktur wystawionych w ostatnim tygodniu i zapisanie wyników do pliku:
```bash
$ kcksefcli SzukajFaktur -c kcksefcli.yaml --from "-1week" --to "now" > /tmp/1.json
```

## Konfiguracja
Szczegóły konfiguracji opisano w pliku [Konfiguracja](docs/Configuration.md).
**Utworzenie pliku konfiguracyjnego z poświadczeniami jest niezbędne, aby korzystać z komend łączących się bezpośrednio z serwerami KSeF.**

## Użycie

Ogólna składnia poleceń `kcksefcli` jest następująca:

```bash
kcksefcli <polecenie> [opcje]
```

Szczegółowy opis konfiguracji profili, globalnych opcji i pamięci podręcznej znajdziesz w dokumencie: [**Konfiguracja**](docs/Configuration.md).



## Polecenia

  - [`CheckAuthNip`](docs/CheckAuthNip.md) - Check if NIP from authentication (token or certificate) matches NIP in configuration.
  - [`DodajPozycjeNaFakturze`](docs/DodajPozycjeNaFakturze.md) - Add a new item to an existing KSeF XML invoice.
  - [`GetFaktura`](docs/GetFaktura.md) - Get a single invoice by KSeF number
  - [`LinkDoFaktury`](docs/LinkDoFaktury.md) - Generate a link to an invoice
  - [`LinkWeryfikacjiFaktury`](docs/LinkWeryfikacjiFaktury.md) - Generuje link weryfikacji faktury (KOD II).
  - [`NowaFaktura`](docs/NowaFaktura.md) - Create a new KSeF XML invoice from a YAML specification.
  - [`NowyCertyfikat`](docs/NowyCertyfikat.md) - Generate a new KSeF certificate.
  - [`ParseDate`](docs/ParseDate.md) - Parse a date string and output it in ISO 8601 format or seconds since epoch.
  - [`PobierzCertyfikat`](docs/PobierzCertyfikat.md) - Retrieve KSeF certificate content by serial number.
  - [`PobierzFaktury`](docs/PobierzFaktury.md) - Download invoices based on search criteria.
  - [`PobierzInfoONip`](docs/PobierzInfoONip.md) - Retrieve NIP information from the government API.
  - [`PokazLimity`](docs/PokazLimity.md) - Show limits for the current context, subject and attachment permission status.
  - [`PrintConfig`](docs/PrintConfig.md) - Print the active configuration
  - [`PrzeslijFaktury`](docs/PrzeslijFaktury.md) - Upload invoices in XML format.
  - [`QRDoFaktury`](docs/QRDoFaktury.md) - Generate a QR code for an invoice and save it to a file
  - [`QRWeryfikacjiFaktury`](docs/QRWeryfikacjiFaktury.md) - Generate a verification QR code (KOD II) for an invoice and save it to a file.
  - [`SprawdzLimitCertyfikatow`](docs/SprawdzLimitCertyfikatow.md) - Check available certificate limits.
  - [`SzukajFaktur`](docs/SzukajFaktur.md) - Query invoice metadata
  - [`TestAuth`](docs/TestAuth.md) - Authenticate using configured method
  - [`TestCertAuth`](docs/TestCertAuth.md) - Authenticate using a certificate
  - [`TestTokenAuth`](docs/TestTokenAuth.md) - Authenticate using a KSeF token
  - [`TestTokenRefresh`](docs/TestTokenRefresh.md) - Refresh an existing session token
  - [`UniewaznijCertyfikat`](docs/UniewaznijCertyfikat.md) - Revoke a KSeF certificate.
  - [`WeryfikujXML`](docs/WeryfikujXML.md) - Validate KSeF XML invoice against the XSD schema.
  - [`WylistujCertyfikaty`](docs/WylistujCertyfikaty.md) - List KSeF certificate metadata.
  - [`WystawFaktureOffline`](docs/WystawFaktureOffline.md) - Convert KSeF XML invoice to PDF, adding an offline verification QR code (KOD II).
  - [`WystawKorekte`](docs/WystawKorekte.md) - Issue a correction invoice based on an input XML.
  - [`WystawPodobnaFakture`](docs/WystawPodobnaFakture.md) - Create a new KSeF XML invoice based on an existing one with updated dates.
  - [`XML2PDF`](docs/XML2PDF.md) - Convert KSeF XML invoice to PDF.
  - [`XMLExtract`](docs/XMLExtract.md) - Extracts a value from an XML file using an XPath expression.
  - [`XMLRemoveNamespace`](docs/XMLRemoveNamespace.md) - Removes namespaces from an XML invoice and sets a default namespace.

## Rozwój

Rozwój oryginalnego projektu odbywa się na GitLabie. Ten fork jest utrzymywany osobno.

### Wymagania

| Narzędzie | Po co | Wymagane |
|---|---|---|
| **.NET SDK 10** | Projekt kompiluje się na `net6.0;net10.0`, a testy na `net10.0`. SDK 10 zbuduje obie wersje docelowe — nie trzeba osobno instalować SDK 6. | Tak |
| **git** | Klient KSeF jest submodułem. | Tak |
| **bash** + **curl** lub **wget** | Testy czarnoskrzynkowe pobierają przy pierwszym uruchomieniu bibliotekę `L_lib.sh`. | Do testów |
| Dostęp sieciowy do `api.nuget.org` i `github.com` | Paczki NuGet, submoduł, `L_lib.sh` oraz generator PDF. | Tak |
| **jq** | Wyłącznie do przykładów użycia z tego README. Testy używają własnego zamiennika `tests/jq_sed.sh` i nie wymagają `jq`. | Nie |
| **node** / **npx** | Tylko dla `XML2PDF` na platformach bez gotowej binarki generatora (np. macOS). Na Linuksie i Windowsie pobierana jest przypięta binarka. | Nie |
| **patchelf** | Tylko dla `make nix-fix` na NixOS. | Nie |

W Debianie/Ubuntu SDK jest w archiwum dystrybucji:

```bash
sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0 git curl jq
```

Jeśli `apt-get install` kończy się błędem 404, najpierw wykonaj `apt-get update` — indeks
pakietów potrafi wskazywać wersję, której nie ma już w repozytorium.

### Przygotowanie środowiska

```bash
# Sklonuj repozytorium i przejdź na gałąź roboczą
git clone <adres-repozytorium> kcksefcli
cd kcksefcli
git checkout claude/ksef-cli-evaluation-d8qhpu

# Inicjalizacja i pobranie zawartości niezbędnych submodułów (zależności)
git submodule update --init --recursive

# Pobranie paczek .NET i budowa projektu (wszystkie wersje docelowe)
dotnet build

# Uruchomienie aplikacji
# -f net10.0 jest konieczne: projekt ma wiele wersji docelowych i bez tego
# `dotnet run` odmawia uruchomienia
dotnet run --project src/KCKSeFCli -f net10.0 -- <polecenie> [opcje]
```

Budowa wypisuje ok. 100 ostrzeżeń `NU1903` pochodzących z submodułu, który deklaruje podatną
wersję `System.Security.Cryptography.Xml`. Nie psują one budowy: `src/KCKSeFCli.csproj`
przypina bezpieczną wersję `10.0.10`, a przypięcie jest celowo trwałe, bo wersja deklarowana
przez klienta wciąż jest podatna.

### Testy

Są dwa zestawy testów i oba powinny przechodzić na czystym drzewie.

```bash
# Testy jednostkowe i regresyjne (C#, xUnit)
dotnet test tests/KCKSeFCli.Tests/KCKSeFCli.Tests.csproj

# Testy czarnoskrzynkowe CLI, uruchamiane na zbudowanej binarce
dotnet publish src/KCKSeFCli/KCKSeFCli.csproj -c Release -r linux-x64 -f net10.0 -o dist
./tests/unit.sh ./dist/kcksefcli
```

Stan oczekiwany: **98 testów C#** oraz **40 testów CLI**, wszystkie zielone przy działającym
dostępie do sieci. Każda poprawka bezpieczeństwa ma własny test regresyjny — opis, czego dana
poprawka broni, znajduje się w komentarzu na początku odpowiedniego pliku
w `tests/KCKSeFCli.Tests/`.

Dwa testy CLI odpytują rządowe API `wl-api.mf.gov.pl` (`clitest_pobierz_info_o_nip`
i `clitest_nowa_faktura_nip_lookup`) i bez dostępu do tego hosta kończą się błędem — to
ograniczenie środowiska, nie regresja.

Sprawdzają one przy tym **dane pobierane na żywo z rejestru**: `clitest_nowa_faktura_nip_lookup`
oczekuje, że NIP `5260202588` rozwinie się dokładnie do `'KAMYK' SPÓŁKA Z OGRANICZONĄ
ODPOWIEDZIALNOŚCIĄ`, `LITERACKA 21/24, 01-864 WARSZAWA`. Zmiana danych rejestrowych tej spółki
zepsuje test, choć w kodzie nic nie będzie nie tak — zanim zaczniesz szukać błędu, sprawdź, co
zwraca API.

Testy w `tests/integration.sh` wymagają prawdziwych poświadczeń KSeF i celowo kończą się
błędem bez nich. Konfigurację umieść w `secrets/kcksefcli.yaml` albo
`.git/KSEF/kcksefcli.yaml`; oba wzorce są już w `.gitignore`.

Skróty w `Makefile`:

```bash
make build        # budowa
make test         # UWAGA: uruchamia `dotnet format`, który MODYFIKUJE pliki źródłowe
make test-format  # sprawdzenie formatowania bez modyfikacji
```

**`make test` przeformatuje ci drzewo.** Zależy od celu `format`, który uruchamia
`dotnet format` bez `--verify-no-changes`, więc przepisuje pliki. Obecnie dotyka ok. 30 plików,
w tym te przeniesione żywcem z repozytorium klienta (`Utils/AsyncPollingUtils.cs`,
`Utils/BatchSessionUtils.cs`, `Utils/KsefRateLimitWrapper.cs`). Ta rozbieżność formatowania
istniała przed tą gałęzią — pliki dodane i zmienione tutaj przechodzą `dotnet format` bez
zmian. Nie została naprawiona celowo: przeformatowanie kopii z upstreamu utrudniłoby przyszłą
synchronizację z `CIRFMF/ksef-client-csharp`.

Do samego uruchomienia testów używaj więc wprost `dotnet test`, a `make test` tylko wtedy, gdy
świadomie chcesz też przeformatować kod. Zadanie `.format_check` w `.gitlab-ci.yml` jest
szablonem (nazwa zaczyna się od kropki), więc CI go nie uruchamia.

### Praca z Claude Code CLI

Ustawienia projektu leżą w `.claude/settings.local.json`. Poza wymaganiami z tabeli wyżej
warto mieć:

```bash
# Claude Code CLI
npm install -g @anthropic-ai/claude-code

# Przydatne przy ręcznym sprawdzaniu wyników
sudo apt-get install -y jq
```

- **.NET SDK 10** — najważniejsza pozycja. Bez niego Claude Code nie zbuduje projektu ani nie
  uruchomi testów, więc nie zweryfikuje własnych zmian i będzie zgadywać zamiast sprawdzać.
- **jq** — do ręcznego oglądania wyjścia JSON z `SzukajFaktur` czy `PrintConfig --json`.
  Same testy go nie potrzebują.
- **ripgrep** — Claude Code ma własny, więc instalacja systemowa nie jest konieczna; przydaje
  się, jeśli chcesz przeszukiwać repozytorium tak samo z własnej powłoki.

Kilka rzeczy specyficznych dla tego repozytorium, które warto wiedzieć przed pierwszą sesją:

- **Zawsze buduj i testuj przed commitem.** `TreatWarningsAsErrors` jest włączone, więc nowe
  ostrzeżenie zatrzymuje budowę.
- **Buduj całe rozwiązanie (`dotnet build`), nie tylko `-f net10.0`.** Wersja `net6.0` używa
  starszych API i łatwo ją zepsuć niezauważenie — CI buduje ją tylko w zadaniu wydania dla
  Windows x86.
- **Pierwsze użycie `XML2PDF` pobiera ok. 74 MB** generatora PDF do `~/.cache/kcksefcli`.
  Plik jest weryfikowany sumą SHA-256 przed uruchomieniem; przy zmianie wersji generatora
  trzeba zaktualizować przypięcie w `XML2PDFCommand` (patrz [XML2PDF](docs/XML2PDF.md)).
- **`tests/L_lib.sh` jest w `.gitignore`** i pobierany przy pierwszym uruchomieniu testów.
  Nie dodawaj go do repozytorium.
- **Nie uruchamiaj poleceń zmieniających stan na profilu produkcyjnym.** Do pracy używaj
  profilu `test` lub `demo`; `PrzeslijFaktury` i `UniewaznijCertyfikat` wywołują skutki
  nieodwracalne po stronie KSeF.

## Uwierzytelnianie w KSeF

Szczegółowe informacje na temat mechanizmów uwierzytelniania w Krajowym Systemie e-Faktur można znaleźć w oficjalnej dokumentacji: [Uwierzytelnianie w KSeF](https://github.com/CIRFMF/ksef-docs/blob/main/uwierzytelnianie.md).

Dokumentacja KSeF API: [https://api-test.ksef.mf.gov.pl/docs/v2/index.html](https://api-test.ksef.mf.gov.pl/docs/v2/index.html).

Artykuł o problemach z namespace w KSeF: [https://ksbot.pl/api/ksef-api-xml-namespace-problemy/](https://ksbot.pl/api/ksef-api-xml-namespace-problemy/).

## Autor i Licencja

Program napisany przez Kamila Cukrowskiego.
Licencja: [GPLv3](LICENSE.md).
