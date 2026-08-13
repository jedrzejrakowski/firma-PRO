# Uruchomienie Firma PRO na własnym komputerze (Windows)

Ta instrukcja jest dla osoby, która chce **zobaczyć działający program**, a nie
dla programisty. Nie trzeba niczego kompilować ani znać się na bazach danych —
wszystko robi jedno polecenie.

Całość zajmuje kwadrans, z czego większość to czekanie na pobieranie.

---

## 1. Zainstaluj Docker Desktop

Docker to program, który uruchamia wszystkie potrzebne części naraz: bazę
danych, sam program i pośrednika podającego stronę po HTTPS. Bez niego
trzeba by instalować każdą z nich osobno.

1. Wejdź na <https://www.docker.com/products/docker-desktop/> i pobierz
   **Docker Desktop for Windows**.
2. Zainstaluj i **uruchom ponownie komputer**, jeśli instalator o to poprosi
   (dokłada składnik systemu o nazwie WSL 2).
3. Uruchom Docker Desktop i poczekaj, aż w lewym dolnym rogu okna pojawi się
   zielony napis **Engine running**.

Docker Desktop musi być uruchomiony za każdym razem, gdy chcesz korzystać
z programu. Można ustawić, żeby startował razem z Windows.

## 2. Pobierz kod programu

1. Wejdź na
   <https://github.com/jedrzejrakowski/firma-PRO/tree/claude/excel-invoicing-ksef-koxjud>
2. Zielony przycisk **Code** → **Download ZIP**.
3. Rozpakuj archiwum, na przykład do `C:\firma-pro`.

> **Uwaga na gałąź.** Cała praca jest na gałęzi
> `claude/excel-invoicing-ksef-koxjud`, a nie na głównej. Odnośnik wyżej
> prowadzi już we właściwe miejsce — jeśli pobierzesz ZIP ze strony głównej
> repozytorium, dostaniesz starszą wersję bez faktur zaliczkowych
> i bez ekranu sprawdzania połączenia z KSeF.

## 3. Uzupełnij ustawienia

W rozpakowanym folderze wejdź do `wdrozenie`. Znajdziesz tam plik
`.env.przyklad`.

1. Skopiuj go i zmień nazwę kopii na **`.env`** (sama kropka na początku,
   bez rozszerzenia).
   Windows potrafi ukrywać rozszerzenia — jeśli plik nazwie się `.env.txt`,
   nic nie zadziała. W Eksploratorze: *Widok → Pokaż → Rozszerzenia nazw plików*.
2. Otwórz `.env` w Notatniku i uzupełnij **sześć** pozycji:

```
ADRES=localhost

HASLO_BAZY=cokolwiek-dlugiego-i-losowego-nikt-tego-nie-wpisuje

KONTO_EMAIL=twoj@adres.pl
KONTO_HASLO=twoje-haslo-min-12-znakow
FIRMA_NAZWA=Moja Firma
FIRMA_NIP=8888888888
```

Trzy rzeczy, na których łatwo się potknąć:

- **Hasło musi mieć co najmniej 12 znaków.** Krótsze program odrzuci przy
  starcie i wyłączy się z czytelnym komunikatem — to celowe, bo to hasło
  do ksiąg firmy.
- **NIP musi mieć poprawną sumę kontrolną.** `8888888888` jest poprawny
  i zgodny z numerem, na który generuje się token w testowym KSeF — dzięki
  temu nie trzeba go potem zmieniać.
- Pozostałych pozycji (poczta) **nie musisz wypełniać**. Bez nich program
  działa, tylko nie wyśle e-maili.

## 4. Uruchom

Kliknij prawym przyciskiem na folderze `wdrozenie` → **Otwórz w terminalu**
(albo uruchom PowerShell i przejdź tam poleceniem `cd C:\firma-pro\wdrozenie`).

Wpisz:

```powershell
docker compose up -d
```

**Pierwsze uruchomienie trwa kilka minut** — Docker pobiera bazę danych
i buduje program. Kolejne są już natychmiastowe.

Gdy polecenie się skończy, wpisz w przeglądarce:

```
https://localhost
```

## 5. Przeglądarka ostrzeże o certyfikacie — to normalne

Pod nazwą `localhost` nie da się uzyskać prawdziwego certyfikatu HTTPS, więc
pośrednik wystawia własny, a przeglądarka go nie zna. Kliknij
**Zaawansowane → Przejdź do localhost (niebezpieczne)**.

Na prawdziwej domenie tego ostrzeżenia nie będzie — certyfikat pobiera się
wtedy sam i za darmo.

## 6. Zaloguj się i wklej token

Zaloguj się adresem i hasłem, które wpisałeś w `.env` jako `KONTO_EMAIL`
i `KONTO_HASLO`.

**Ustawienia** znajdziesz w **górnym pasku** — obok „Faktury", „Zakupy",
„Rejestr VAT". Widzi je wyłącznie właściciel firmy; księgowy i osoba
z podglądem nie mają tam wstępu, bo tam leży token KSeF.

Na dole strony ustawień jest sekcja **Dostęp do KSeF**: wybierz środowisko
*Testowe*, wklej token i zapisz. Potem odnośnik **Sprawdź połączenie z KSeF**
przeprowadzi sprawdzenie krok po kroku.

---

## Co dalej

| Chcę… | Polecenie (w folderze `wdrozenie`) |
|---|---|
| zatrzymać program | `docker compose down` |
| uruchomić ponownie | `docker compose up -d` |
| zobaczyć, co się dzieje | `docker compose logs -f program` |
| sprawdzić, czy działa | `docker compose ps` |

Dane zostają w Dockerze między uruchomieniami — zatrzymanie programu
niczego nie kasuje.

## Gdy coś nie zadziała

- **`docker: command not found`** — Docker Desktop nie jest uruchomiony albo
  trzeba otworzyć terminal na nowo po instalacji.
- **`ustaw HASLO_BAZY w pliku .env`** — plik `.env` nie został znaleziony
  (najczęściej nazywa się `.env.txt`) albo pozycja została pusta.
- **Program startuje i od razu się wyłącza** — zajrzyj do
  `docker compose logs program`. Najczęstsza przyczyna to hasło krótsze
  niż 12 znaków albo NIP z błędną sumą kontrolną; program mówi o tym wprost
  w ostatniej linii dziennika.
- **Strona się nie otwiera** — sprawdź `docker compose ps`; wszystkie cztery
  usługi powinny być w stanie `running`. Port 443 potrafi być zajęty przez
  inny program.

> **Zastrzeżenie.** Całego zestawu nie udało się uruchomić w środowisku,
> w którym program powstawał — zablokowane jest tam pobieranie obrazów
> z Docker Huba. Sam plik `docker-compose.yml` przechodzi sprawdzenie
> składni, a obraz programu buduje się i startuje poprawnie, ale pierwsze
> `docker compose up` u Ciebie będzie zarazem pierwszym prawdziwym
> uruchomieniem całości. Jeśli coś nie zagra, treść komunikatu wystarczy,
> żeby to naprawić.
