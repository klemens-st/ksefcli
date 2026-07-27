# Zgłoszenia do projektów nadrzędnych

Materiał gotowy do wysłania. **Nic z tego nie zostało jeszcze zgłoszone** — wysyłka wymaga
decyzji człowieka.

## 1. `CIRFMF/ksef-client-csharp` — podatna wersja `System.Security.Cryptography.Xml`

**Wersja:** v2.7.0 (commit `406904d`)
**Plik:** `KSeF.Client/KSeF.Client.csproj`, linie 41, 68, 86, 104

```xml
<PackageReference Include="System.Security.Cryptography.Xml" Version="10.0.7" />
```

Wersja 10.0.7 jest objęta ostrzeżeniem `NU1903` (znana podatność o wysokiej istotności).
Konsekwencje dla każdego projektu korzystającego z tego klienta:

- przy `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` **projekt się nie kompiluje** —
  audyt NuGet podnosi ostrzeżenie do błędu; to był powód pierwszego commita w tym forku,
- bez tego ustawienia pojawia się ok. 100 ostrzeżeń `NU1903`, które zagłuszają realne problemy.

**Proponowana poprawka:** podniesienie do `10.0.10` we wszystkich czterech miejscach. W tym
forku pin działa od `9766644` bez żadnych zmian w kodzie — API jest zgodne.

**Obejście u nas:** jawny `PackageReference` na `10.0.10` w `src/KCKSeFCli/KCKSeFCli.csproj`.
Pin jest trwały do czasu naprawy powyżej.

## 2. `kcksefcli` (oryginał na GitLabie) — poprawki bezpieczeństwa z tego forka

Commity są celowo małe i rozdzielne, żeby dało się je przenieść pojedynczo.

| Commit | Poprawka |
|---|---|
| `9766644` | pin `System.Security.Cryptography.Xml` (bez tego projekt nie buduje się z czystego klona) |
| `0505b10` | walidacja XML wyłącznie względem lokalnego łańcucha XSD, nigdy przez sieć |
| `c236fe1` | niezerowy kod wyjścia, gdy KSeF odrzuci faktury |
| `f2c4b5a` | cache tokenów tworzony atomowo z prawami 0600 |
| `3360ae6` | `PrintConfig` domyślnie maskuje sekrety |
| `55c263b` | sanityzacja identyfikatorów z KSeF używanych jako nazwy plików |
| `eb8eca2` | weryfikacja łańcucha certyfikatu poza środowiskiem testowym |
| `df134c8` | generator PDF przypięty po SHA-256 |
| `5d60159` | naprawa API SHA-256 dla `net6.0` |
| `417a484` | `L_lib.sh` przypięty po SHA-256 przed załadowaniem |
| `6ddd759` | ograniczanie liczby zapytań również dla wyszukiwania i wysyłki |
| `7ab7377` | poprawne pasma stawek VAT i zaokrąglanie w `DodajPozycjeNaFakturze` |
| `f666710` | przeliczanie wszystkich pasm stawek w `WystawKorekte` |
| `520db36` | bramka na operacjach nieodwracalnych w środowisku produkcyjnym |

Dwie zmiany autor oryginału może chcieć odrzucić i warto je zgłosić osobno:

- **`ee09168` — usunięcie `SelfUpdate`.** Polecenie pobierało i uruchamiało artefakt bez
  weryfikacji, a nie istnieje stabilny artefakt, który dałoby się przypiąć. Dla oryginału
  alternatywą jest publikowanie wydań z sumami kontrolnymi zamiast usuwania polecenia.
- **`520db36` — bramka potwierdzenia.** Zmienia zachowanie dla użytkownika pracującego
  nieinteraktywnie w środowisku produkcyjnym.

Zmiany warte zgłoszenia niezależnie od reszty, bo to zwykłe błędy:

- `README.md` używał `jq -r '.Invoices[0].KsefNumber'`, a `SzukajFaktur` serializuje gołą
  tablicę — poprawnie `.[0].KsefNumber` (commit `46390d8`).
- `dotnet run --project src/KCKSeFCli -- <polecenie>` z README nigdy nie działał: projekt ma
  wiele wersji docelowych, więc konieczne jest `-f net10.0` (commit `8b6bfd1`).
- `.format_check` w `.gitlab-ci.yml` nigdy się nie uruchamia — kropka na początku nazwy czyni
  z niego szablon zadania, nie zadanie. (Ten fork usunął `.gitlab-ci.yml` w całości, bo korzysta
  wyłącznie z GitHub Actions; uwaga dotyczy pliku w repozytorium oryginalnym.)
