[Powrót do strony głównej](../README.md)

# Polecenie: `WystawFaktureOffline`

> [!NOTE]
> To polecenie działa w trybie **offline** i **nie łączy się** bezpośrednio z serwerami KSeF. Wykorzystuje dane klucza prywatnego certyfikatu zdefiniowane w profilu (`kcksefcli.yaml`), aby złożyć odpowiedni podpis bez wywoływania zapytań sieciowych.


Konwertuje fakturę KSeF XML na PDF, dodając kod QR weryfikacji offline (KOD II).

> **Ważne wymaganie:** Aby poprawnie wygenerować podpisany kod QR w trybie offline, musisz posiadać w konfiguracji aktywnego profilu poprawny certyfikat (sekcja `certificate` w `kcksefcli.yaml`), który został uprzednio wygenerowany w systemie KSeF z przeznaczeniem do **wystawiania faktur w trybie offline**. Bez dostępu do klucza prywatnego tego certyfikatu, narzędzie nie będzie w stanie podpisać sumy kontrolnej dokumentu zgodnie ze specyfikacją KOD II.

**Użycie:**
```bash
kcksefcli WystawFaktureOffline faktura.xml faktura.pdf
```

**Argumenty:**

| Argument      | Opis                                   | Wymagane |
|---------------|----------------------------------------|----------|
| `InputFile`   | Ścieżka do pliku XML z fakturą.        | Tak      |
| `OutputFile`  | Ścieżka wyjściowa dla pliku PDF. Jeśli nie zostanie podana, plik powstanie obok pliku wejściowego, z rozszerzeniem zmienionym na `.pdf`. | Nie      |

**Opcje:**

| Opcja      | Opis                                     |
|------------|------------------------------------------|
| `--nrKSeF` | Numer KSeF faktury do osadzenia w PDF.   |

---


## Konfiguracja i Uwierzytelnianie

To polecenie **nie łączy się** z serwerami KSeF, działa w pełni lokalnie. System profili i obsługa plików (`kcksefcli.yaml`) jest jednak potrzebna po to, aby uzyskać dostęp do zdefiniowanego w profilu klucza prywatnego, by poprawnie podpisać wystawiony element offline.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
