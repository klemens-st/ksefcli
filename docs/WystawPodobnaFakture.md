[Powrót do strony głównej](../README.md)

# Polecenie: `WystawPodobnaFakture`

Tworzy nową fakturę XML KSeF na podstawie istniejącego pliku faktury i aktualizuje wybrane daty. Skutecznie ułatwia tworzenie cyklicznych faktur na podstawie gotowego szablonu z przeszłości.

Uaktualnia automatycznie datę wytworzenia faktury, datę wystawienia (`P_1`), datę dokonania lub zakończenia dostawy towarów/wykonania usług (`P_6`) oraz numer faktury (`P_2`).

> **Uwaga na numer faktury.** Warunkiem przenumerowania jest wyłącznie to, że `P_2` zaczyna się
> od `FV/`. Cała dotychczasowa wartość jest wtedy **zastępowana** wzorcem `FV/yyyyMMdd/01`
> z nową datą wystawienia. Numer taki jak `FV/2026/0042` również spełnia ten warunek, więc
> zostanie zamieniony na `FV/<data>/01`, a dotychczasowy sposób numerowania — w tym licznik
> `0042` — przepadnie. Jeżeli używasz własnego schematu numeracji zaczynającego się od `FV/`,
> sprawdź `P_2` w wynikowym pliku przed wysłaniem faktury.

**Użycie:**
```bash
kcksefcli WystawPodobnaFakture <plik-wejsciowy-xml> <plik-wyjsciowy-xml> [--data-wystawienia <data>] [--data-wykonania <data>]
```

**Argumenty:**

| Argument             | Opis                                                   | Wymagane |
|----------------------|--------------------------------------------------------|----------|
| `plik-wejsciowy-xml` | Ścieżka do istniejącego pliku wejściowego XML faktury. | Tak      |
| `plik-wyjsciowy-xml` | Ścieżka dla nowo wygenerowanego pliku XML.             | Tak      |

**Opcje:**

| Opcja                  | Opis                                                                                          | Domyślnie    |
|------------------------|-----------------------------------------------------------------------------------------------|--------------|
| `--data-wystawienia`   | Nowa data wystawienia faktury (pole P_1). Format `yyyy-MM-dd`. Jeśli nie podano, użyje dzisiejszej daty. | Dziś         |
| `--data-wykonania`     | Nowa data wykonania usługi/dostawy (pole P_6). Format `yyyy-MM-dd`. Jeśli nie podano, użyje dzisiejszej daty. | Dziś         |
