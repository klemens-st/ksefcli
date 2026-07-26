# Konfiguracja


Przed rozpoczęciem pracy z `kcksefcli`, należy skonfigurować aplikację, tworząc plik `kcksefcli.yaml`. Domyślna lokalizacja tego pliku zależy od systemu operacyjnego:
- Linux: `$XDG_CONFIG_HOME/kcksefcli/kcksefcli.yaml` lub `~/.config/kcksefcli/kcksefcli.yaml`
- Windows: `%LOCALAPPDATA%\kcksefcli\kcksefcli.yaml`
- macOS: `~/Library/Application Support/kcksefcli/kcksefcli.yaml`

Możesz również wskazać inną lokalizację pliku konfiguracyjnego za pomocą globalnej opcji `--config` (lub `-c`) lub ustawiając zmienną środowiskową `KCKSEFCLI_CONFIG`.

Plik ten zawiera profile, które umożliwiają zarządzanie różnymi poświadczeniami i środowiskami KSeF. **Pamiętaj, że wartości konfiguracyjne z pliku mogą być nadpisane przez globalne opcje linii komend lub zmienne środowiskowe, zgodnie z kolejnością priorytetów opisaną w sekcji [Opcje Globalne](#opcje-globalne).**

### Struktura pliku `kcksefcli.yaml`

```yaml
active_profile: <nazwa_aktywnego_profilu>
profiles:
  <nazwa_profilu_1>:
    environment: <srodowisko>
    nip: <nip_podmiotu>
    token: <token_autoryzacyjny>
    certificate:
      private_key: <zawartosc_klucza_prywatnego>
      private_key_file: <sciezka_do_klucza_prywatnego>
      certificate: <zawartosc_certyfikatu_publicznego>
      certificate_file: <sciezka_do_certyfikatu_publicznego>
      password: <haslo_do_klucza_prywatnego>
      password_env: <zmienna_srodowiskowa_z_haslem>
      password_file: <sciezka_do_pliku_z_haslem>
      password_cmd: ["<komenda>", "<argument1>", "<argument2>"]
  <nazwa_profilu_2>:
    # ...
```

### Opcje Konfiguracyjne

*   `active_profile`: (Opcjonalnie) Nazwa profilu, który będzie używany domyślnie, jeśli nie zostanie podany za pomocą opcji `--profile`. Jeśli zdefiniowany jest tylko jeden profil, `active_profile` jest ignorowane.
*   `profiles`: Mapa profili konfiguracyjnych.
    *   `<nazwa_profilu>`: Dowolna nazwa identyfikująca profil (np. `dyzio`, `firma_xyz_test`).
        *   `environment`: Środowisko KSeF (`test`, `demo`, `prod`).
        *   `nip`: (Opcjonalnie) Numer Identyfikacji Podatkowej (NIP) podmiotu, którego dotyczy profil. Jeśli nie zostanie podany, zostanie automatycznie wyciągnięty z tokenu autoryzacyjnego lub certyfikatu.
        *   Należy zdefiniować **jedną** z poniższych metod uwierzytelniania:
            *   `token`: Token autoryzacyjny sesji.
            *   `certificate`: Dane certyfikatu kwalifikowanego.
                *   `private_key`: Zawartość klucza prywatnego.
                *   `private_key_file`: Ścieżka do klucza prywatnego (plik `.pem` lub `.pfx`). Można użyć `~` jako skrótu do katalogu domowego.
                *   `certificate`: Zawartość certyfikatu publicznego.
                *   `certificate_file`: Ścieżka do certyfikatu publicznego. Można użyć `~` jako skrótu do katalogu domowego.
                *   `password`: Hasło do klucza prywatnego.
                *   `password_env`: Nazwa zmiennej środowiskowej, która przechowuje hasło do klucza prywatnego.
                *   `password_file`: Ścieżka do pliku z hasłem do klucza prywatnego.
                *   `password_cmd`: Tablica ciągów znaków (komenda i argumenty) do wykonania w celu pobrania hasła. Hasło zostanie odczytane ze standardowego wyjścia (stdout) komendy. Opcja ta jest w konflikcie z `password`, `password_env` oraz `password_file`.

### Przykład Konfiguracji

Poniższy przykład demonstruje konfigurację z wieloma profilami dla różnych podmiotów i środowisk.

```yaml
---
active_profile: firma1
profiles:
  firma1:
    environment: test
    nip: '12312312312'
    token: fdsafa
  firma2:
    environment: demo
    nip: '12312312312'
    token: fdsfa
  firma3:
    environment: prod
    nip: '23434545676'
    token: fdasfa
  cert_auth_example:
    environment: prod
    nip: '1234567890'
    certificate:
      private_key_file: '~/certs/my_private_key.pem'
      certificate_file: '~/certs/my_certificate.pem'
      password_env: 'MY_PASSWORD_ENV'

```

W tym przykładzie:
- Domyślnym profilem jest `firma1`.
- Zdefiniowano trzy profile (`firma1`, `firma2`, `firma3`) używające uwierzytelniania tokenem na środowisku testowym dla dwóch różnych NIP-ów.
- Profil `cert_auth_example` używa uwierzytelniania certyfikatem na środowisku produkcyjnym. Hasło do certyfikatu zostanie odczytane ze zmiennej środowiskowej `MY_PASSWORD_ENV`.

## Pamięć podręczna (Cache)

Domyślnie `kcksefcli` stosuje mechanizm pamięci podręcznej (cache) dla tokenów sesyjnych.
Po pomyślnym uwierzytelnieniu (np. przez polecenia `Auth`, `TokenAuth` lub `CertAuth`), nowo uzyskany token sesyjny jest zapisywany w lokalnym pliku cache. Przy kolejnych wywołaniach komend, narzędzie w pierwszej kolejności próbuje odczytać i wykorzystać już zapisany token, aby uniknąć konieczności powtarzania procesu logowania. Zmniejsza to liczbę zapytań do serwerów KSeF, przyspiesza działanie narzędzia oraz zmniejsza ryzyko przekroczenia limitów zapytań API.
Tokeny są również automatycznie odświeżane w tle, gdy system wykryje, że zbliża się koniec ich ważności.

### Lokalizacja pliku

Domyślna lokalizacja pliku, w którym przechowywane są tokeny sesyjne, to:
- W systemach Linux / macOS: `$HOME/.cache/kcksefcli/tokenstore.json`
- W systemach Windows: `%LOCALAPPDATA%\kcksefcli\tokenstore.json`

### Uprawnienia do pliku

**Tokeny są przechowywane jawnym tekstem** — plik cache zawiera zarówno token dostępowy, jak i
token odświeżający, w postaci nadającej się do bezpośredniego użycia. Każdy, kto odczyta ten
plik, może wystawiać faktury w Twoim imieniu do czasu wygaśnięcia tokenów.

Dlatego w systemach uniksowych plik jest tworzony z uprawnieniami `0600` (odczyt i zapis
wyłącznie dla właściciela), a katalog — o ile to `kcksefcli` go zakłada — z uprawnieniami
`0700`. Uprawnienia są nadawane w momencie tworzenia pliku, nie po zapisie, więc token nigdy
nie trafia do pliku dostępnego dla innych użytkowników. Plik pozostały po starszej wersji
narzędzia zostaje przy najbliższym uruchomieniu naprawiony, z odpowiednim ostrzeżeniem.

Jeżeli wskażesz opcją `--cache` istniejący katalog, jego uprawnienia nie są zmieniane — może
on należeć do kogoś innego. Zamiast tego pojawi się ostrzeżenie. Sam plik i tak pozostaje
ograniczony do właściciela.

Jeśli tokeny nie mają być zapisywane na dysku w ogóle, użyj `--no-tokencache`.

### Opcje Cache'owania i konfiguracji

Podczas wywoływania komend korzystających z konfiguracji, dostępne są globalne opcje pozwalające na sterowanie zachowaniem pamięci podręcznej oraz środowiskiem logowania.

Dodatkowo zachowaniem tych opcji można sterować poprzez zmienne środowiskowe:
- `$KCKSEFCLI_CONFIG` - domyślna ścieżka do pliku `kcksefcli.yaml`.
- `$KCKSEFCLI_ACTIVE` - nazwa aktywnego profilu, używana gdy nie podano jawnie opcji `--active`.

**Wyłączające się metody konfiguracji:**
Możesz sterować konfiguracją używając opcji `--config`/`--active` LUB definiować ad-hoc poświadczenia opcjami takimi jak `--environment`, `--token`. Próba łączenia opcji ad-hoc z ładowaniem z pliku skutkuje błędem.

| Opcja | Zmienna środowiskowa | Opis | Domyślnie | Konfliktuje z |
| :--- | :--- | :--- | :--- | :--- |
| `-c`, `--config` | `$KCKSEFCLI_CONFIG` | Wskazuje plik `kcksefcli.yaml` zawierający definicje profili. | `~/.config/kcksefcli/kcksefcli.yaml` (zależnie od systemu) | Ad-hoc opcje profilu |
| `-a`, `--active` | `$KCKSEFCLI_ACTIVE` | Wybiera z pliku wskazanego w `--config` profil o zadanej nazwie. | `active_profile` z pliku YAML lub pierwszy wylistowany profil | Ad-hoc opcje profilu |
| `--cache` | | Ścieżka do pliku do zapisu oraz odczytu tokenów sesyjnych (cache). | `~/.cache/kcksefcli/tokenstore.json` (Linux/Mac) | Brak |
| `--no-tokencache` | | Całkowicie wyłącza odczyt i zapis tokenów z/do pamięci podręcznej na czas trwania bieżącego wywołania komendy. | `false` | Brak |
| `--environment` | | Ustawia wybrane środowisko KSeF dla wywołania ad-hoc (np. `test`, `demo`). | | `--config`, `--active` |
| `--token` | | Używa wskazanego tokena autoryzacyjnego (numer NIP wyciągany jest z tokenu). Tworzy profil ad-hoc. | | `--config`, `--active` |

### Współdziałanie z opcjami konfiguracyjnymi

Pamięć podręczna (podobnie jak wszystkie opcje konfiguracyjne poświadczeń) jest ściśle powiązana z koncepcją profilu, co zapobiega pomyłkowemu używaniu tokenów jednego środowiska (np. `test`) na innym (np. `prod`).
Plik tokenów przechowuje słownik przypisujący weryfikowany "aktywny profil" do danego, wygenerowanego tokenu sesji, więc każdorazowe podanie parametru `--active` (np. `--active profil_firmaA` a następnie `--active profil_firmaB`) będzie bezpiecznie przełączać również aktywne sesje cache na odpowiednie dla danego uwierzytelnienia.

