[Powrót do strony głównej](../README.md)

# Polecenie: `XML2PDF`

Konwertuje poprawną fakturę KSeF w formacie XML (lub plik UPO - Urzędowego Poświadczenia Odbioru) na czytelny dla człowieka plik PDF.

Silnik generujący i renderujący widoki faktur do formatu PDF korzysta w dużej mierze z rozwiązań i szablonów open-source z zewnętrznego projektu `ksef-pdf-converter`. 

Zależnie od udostępnionych flag, do faktury można dołączyć kody QR i numer KSeF.

**Użycie:**
```bash
kcksefcli XML2PDF <plik-xml> [<plik-wyjsciowy-pdf>]
```

**Argumenty:**

| Argument               | Opis                                                      | Wymagane |
|------------------------|-----------------------------------------------------------|----------|
| `plik-xml`             | Ścieżka do istniejącego pliku wejściowego XML faktury.    | Tak      |
| `plik-wyjsciowy-pdf`   | Opcjonalna ścieżka dla docelowego pliku `.pdf`.           | Nie      |

**Opcje:**

| Opcja       | Opis                                                 |
|-------------|------------------------------------------------------|
| `--upo`     | Informuje konwerter, aby użył szablonu UPO zamiast FA. |
| `--nrKSeF`  | Numer nadany przez KSeF fakturze, dołączany na wydruku w PDF. |
| `--qrCode`  | Bezpośredni URL z kodem QR do osadzenia w wydruku.   |
| `--qrCode2` | Drugi URL z kodem QR do osadzenia w dokumencie PDF.   |

---

## Generator PDF i jego weryfikacja

Renderowanie wykonuje osobny program, `ksef-pdf-generator`, pobierany przy pierwszym użyciu
z wydania na GitHubie i uruchamiany jako podproces. Dotyczy to również poleceń
`PobierzFaktury --pdf` oraz `PrzeslijFaktury --upopdf`.

Ponieważ pobrany plik jest **wykonywany**, jego zawartość jest przypięta sumą SHA-256 zapisaną
w kodzie (`XML2PDFCommand.LinuxGeneratorSha256` i `WindowsGeneratorSha256`). Suma jest
sprawdzana po pobraniu, a przed nadaniem prawa wykonywania i uruchomieniem. Plik o niezgodnej
sumie zostaje usunięty i nigdy nie trafia do miejsca docelowego.

Pamięć podręczna jest adresowana zawartością, a nie znacznikiem czasu: kopia w
`~/.cache/kcksefcli` jest używana tylko wtedy, gdy jej suma się zgadza, a w przeciwnym razie
plik jest pobierany ponownie. Plik otrzymuje uprawnienia `0700`.

Na platformach bez gotowego wydania (np. macOS) używany jest `npx`, przypięty do identyfikatora
commita, a nie do tagu — tag można przesunąć, commita nie.

### Odświeżanie przypięcia

Po zmianie wersji generatora należy wyliczyć nowe sumy i wpisać je do `XML2PDFCommand`:

```bash
curl -sSL -o gen "https://github.com/Kamilcuk/ksef-pdf-generator/releases/download/<wersja>/ksef-pdf-generator"
sha256sum gen
```

Wydania na GitHubie można podmienić w miejscu, dlatego weryfikacja opiera się na sumie
zapisanej w repozytorium, a nie na samym adresie URL.
