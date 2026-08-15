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
   **Docker Desktop for Windows — AMD64**.

   > „AMD64" to nazwa architektury, a nie producenta procesora — ten sam
   > plik jest dla Intela i dla AMD. Wariant **ARM64** dotyczy wyłącznie
   > laptopów na procesorach Snapdragon (Surface Pro X, „Copilot+ PC").
   > Gdy nie masz pewności: **Win+R** → `msinfo32` → wiersz *Typ systemu*;
   > `x64-based PC` oznacza AMD64.

2. W oknie **Configuration** zostaw zaznaczone **Per-user installation
   (Recommended)** — używa WSL 2 i nie wymaga hasła administratora.
   Skrót na pulpicie warto zostawić: Docker Desktop musi być uruchomiony
   za każdym razem, gdy chcesz korzystać z programu.
3. Zainstaluj i **uruchom ponownie komputer**, jeśli instalator o to poprosi
   (dokłada składnik systemu o nazwie WSL 2).
4. Przy pierwszym uruchomieniu Docker pokaże **Docker Subscription Service
   Agreement** — kliknij **Accept**. Program jest darmowy; płatna
   subskrypcja zaczyna się dopiero powyżej 250 pracowników albo 10 mln USD
   przychodu rocznie.
5. Na ekranie **Welcome to Docker** kliknij **Skip** (link w prawym górnym
   rogu okna). Konto Docker nie jest do niczego potrzebne.
6. Poczekaj, aż w lewym dolnym rogu okna pojawi się zielony napis
   **Engine running**.

> **Na marginesie, dla sprzedaży programu.** Powyższy warunek dotyczy
> *Docker Desktop* — wersji z interfejsem graficznym na Windows i macOS.
> Wdrożenie serwerowe (`docker compose` na Linuksie) opiera się na
> *Docker Engine*, który jest na licencji Apache 2.0 i darmowy niezależnie
> od wielkości firmy. Klient nie potrzebuje więc subskrypcji Dockera,
> żeby używać Firma PRO.

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

## „Virtualization support not detected"

Docker Desktop nie wystartuje, dopóki komputer nie pozwala uruchamiać
maszyn wirtualnych. To najczęstszy problem przy pierwszej instalacji.
Przycisk *Sign in* w tym okienku niczego nie naprawia — konto Docker
nie ma z tym związku.

Najpierw ustal, na czym stoisz: **Ctrl+Shift+Esc** → **Wydajność** →
**Procesor** → wiersz **Wirtualizacja**.

**Wirtualizacja: Wyłączona** — włącz ją w BIOS-ie:

1. Uruchom komputer ponownie i naciskaj **Del** albo **F2** (podpowiedź
   zwykle widać na pierwszym ekranie).
2. Znajdź **Intel Virtualization Technology**, **VT-x**, **SVM Mode**
   albo **AMD-V** — najczęściej w *Advanced*, *CPU Configuration*
   lub *Security*.
3. Ustaw **Enabled**, zapisz i wyjdź (zwykle **F10**).

**Wirtualizacja: Włączona** — wirtualizacja działa, ale brakuje składników
Windows, przez które Docker z niej korzysta.

> **Czym jest WSL i po co go włączamy.** WSL (Windows Subsystem for Linux)
> to składnik samego Windows 11, domyślnie wyłączony — nie osobny program,
> z którego trzeba by korzystać. Kontenery są technologią linuksową, więc
> Docker trzyma pod spodem bardzo lekką maszynę wirtualną z Linuksem i to
> w niej stoi baza danych oraz program. WSL 2 jest mechanizmem, który
> Windows do tego udostępnia. Poniższe polecenia **włączają wyłącznie
> funkcje systemu** — nie instalują Ubuntu ani żadnej dystrybucji. Otwórz **PowerShell jako
administrator** (prawy przycisk na menu Start → *Terminal (Administrator)*)
i wykonaj:

```powershell
dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
```

Obie mają zakończyć się komunikatem *Operacja została ukończona pomyślnie*.
Następnie **uruchom komputer ponownie** — składniki włączają się dopiero
przy starcie systemu. Po ponownym uruchomieniu, znów jako administrator:

```powershell
wsl --update
wsl --set-default-version 2
```

I dopiero wtedy włącz Docker Desktop.

> Dlaczego `dism`, a nie prostsze `wsl --install`: na komputerze, gdzie WSL
> jest już częściowo obecny, `wsl --install` potrafi wypisać samą pomoc
> i nie zrobić nic. Wtedy nie wiadomo, czy polecenie zadziałało.

**Skąd wiadomo, że się udało.** Szukaj w wypisanym tekście linii:

```
The operation completed successfully.
Żądana operacja powiodła się. Zmiany nie odniosą skutku aż do ponownego
uruchomienia systemu.
```

Druga linia brzmi jak ostrzeżenie, a jest potwierdzeniem: składnik został
włączony i czeka na restart. Bywa też, że samo `wsl --update` włączy przy
okazji **VirtualMachinePlatform** — wtedy polecenia `dism` nie mają już nic
do zrobienia i to też jest w porządku.

Dopóki nie uruchomisz komputera ponownie, Docker będzie pokazywał ten sam
błąd, mimo że wszystko jest już włączone.

> Na komputerze służbowym wejście do BIOS-u bywa zablokowane przez dział
> informatyczny. Gdyby tak było, program da się uruchomić bez Dockera —
> patrz [Uruchomienie](../README.md#uruchomienie) — ale wymaga to
> zainstalowania .NET SDK i PostgreSQL osobno.

## „dependency failed to start: container firmapro-baza-1 is unhealthy"

Program i pośrednik zbudowały się poprawnie, ale baza danych nie wstała.
Przyczynę pokazuje:

```powershell
docker compose logs baza
```

Jeśli w dzienniku widać **`invalid locale name`**, masz starą wersję pliku
`docker-compose.yml`. Obraz `postgres:16` ma wygenerowaną wyłącznie
lokalizację `en_US.utf8`, więc zakładanie bazy z `--locale=pl_PL.utf8`
przerywa się błędem. Obecna wersja pliku używa zamiast tego mechanizmu ICU
wbudowanego w PostgreSQL.

Naprawa polega na pobraniu aktualnego `docker-compose.yml` i **skasowaniu
nieudanej bazy** — zwykłe ponowne uruchomienie nie wystarczy, bo katalog
danych został już częściowo utworzony:

```powershell
docker compose down -v
docker compose up -d
```

> `down -v` kasuje wszystkie dane programu. Przy pierwszym uruchomieniu nie
> ma jeszcze czego stracić, ale **później to polecenie usuwa faktury** —
> wtedy używa się samego `docker compose down`.

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
