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

Rozwój odbywa się na GitLabie.

Aby skonfigurować środowisko deweloperskie i uruchomić aplikację, wykonaj następujące kroki:

```bash
# Sklonuj repozytorium
git clone https://gitlab.com/kamcuk/kcksefcli.git
cd kcksefcli

# Inicjalizacja i pobranie zawartości niezbędnych submodułów (zależności)
git submodule update --init --recursive

# Pobranie paczek .NET i budowa projektu
dotnet build

# Uruchomienie aplikacji
dotnet run --project src/KCKSeFCli -- <polecenie> [opcje]
```

## Uwierzytelnianie w KSeF

Szczegółowe informacje na temat mechanizmów uwierzytelniania w Krajowym Systemie e-Faktur można znaleźć w oficjalnej dokumentacji: [Uwierzytelnianie w KSeF](https://github.com/CIRFMF/ksef-docs/blob/main/uwierzytelnianie.md).

Dokumentacja KSeF API: [https://api-test.ksef.mf.gov.pl/docs/v2/index.html](https://api-test.ksef.mf.gov.pl/docs/v2/index.html).

Artykuł o problemach z namespace w KSeF: [https://ksbot.pl/api/ksef-api-xml-namespace-problemy/](https://ksbot.pl/api/ksef-api-xml-namespace-problemy/).

## Autor i Licencja

Program napisany przez Kamila Cukrowskiego.
Licencja: [GPLv3](LICENSE.md).
