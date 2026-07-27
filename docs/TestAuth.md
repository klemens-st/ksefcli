[Powrót do strony głównej](../README.md)

# Polecenie: `TestAuth`

> [!IMPORTANT]
> **Komenda testowa:** To polecenie służy wyłącznie do ręcznego testowania procesu uwierzytelniania. W normalnej pracy z narzędziem nie ma potrzeby wywoływania go jawnie. Aplikacja `kcksefcli` automatycznie zarządza procesem logowania, pobieraniem tokenów sesyjnych oraz ich odświeżaniem w tle przed wykonaniem jakiejkolwiek innej operacji (np. wysyłki lub szukania faktur).
>
> Ta komenda może zostać usunięta w przyszłych wersjach narzędzia.
> Więcej o automatycznym zarządzaniu sesją znajdziesz w dokumencie: [**Konfiguracja**](Configuration.md).

Uwierzytelnia użytkownika na podstawie metody zdefiniowanej w aktywnym profilu (token lub certyfikat) i sprawdza w ten sposób, czy profil działa.

Polecenie **nie wypisuje tokenu** — kończy się kodem `0`, gdy uwierzytelnienie się powiodło,
a kodem `1`, gdy się nie powiodło. Aby zobaczyć sam token, użyj
[`TestTokenAuth`](TestTokenAuth.md) albo [`TestCertAuth`](TestCertAuth.md).

**Użycie:**
```bash
kcksefcli TestAuth -a moj_profil
```

---


## Konfiguracja i Uwierzytelnianie

To polecenie łączy się z serwerami KSeF i w pełni obsługuje system profili, opcje konfiguracji (`kcksefcli.yaml`) oraz automatycznej pamięci podręcznej (cache) tokenów sesyjnych.
Szczegółowe informacje o zarządzaniu sesją, przełączaniu profili i środowisk znajdują się w pliku: [**Konfiguracja**](Configuration.md).
