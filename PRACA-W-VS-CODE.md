# Praca nad programem w VS Code (Windows)

Ta instrukcja jest po to, żeby **otworzyć kod na własnym komputerze,
uruchomić program z podglądem w przeglądarce i móc go zmieniać**.

Zakłada, że masz już Docker Desktop — jeśli nie, wystarczy pierwszy rozdział
[instrukcji uruchomienia](wdrozenie/URUCHOMIENIE-WINDOWS.md).

**Czas:** około 30 minut, głównie na pobieranie.

---

## 1. Zainstaluj trzy programy

| Co | Skąd | Po co |
|---|---|---|
| **.NET SDK 10** | <https://dotnet.microsoft.com/download/dotnet/10.0> — *SDK*, wariant **x64** | kompiluje program; sam „Runtime" nie wystarczy |
| **VS Code** | <https://code.visualstudio.com/> | edytor |
| **Git** | <https://git-scm.com/download/win> | pobieranie kodu i zapisywanie zmian |

W instalatorze Gita wszystkie ustawienia domyślne są w porządku — klikaj
dalej do końca.

Po instalacji **zamknij i otwórz PowerShell od nowa** (inaczej nie zobaczy
nowych programów) i sprawdź:

```powershell
dotnet --version
git --version
```

`dotnet --version` musi pokazać numer zaczynający się od **10**.

## 2. Pobierz kod

W PowerShellu:

```powershell
cd C:\
git clone -b claude/excel-invoicing-ksef-koxjud https://github.com/jedrzejrakowski/firma-PRO.git
cd firma-PRO
code .
```

Ostatnie polecenie otwiera katalog w VS Code.

> **Uwaga na gałąź.** Cała praca jest na gałęzi
> `claude/excel-invoicing-ksef-koxjud`, nie na głównej. Polecenie wyżej
> pobiera od razu właściwą.

Przy pierwszym otwarciu VS Code zaproponuje **zainstalowanie zalecanych
rozszerzeń** — zgódź się. Najważniejsze z nich to **C# Dev Kit**: bez niego
edytor nie podpowie składni ani nie uruchomi testów.

Poczekaj, aż na dolnym pasku zniknie napis o wczytywaniu projektu. Za
pierwszym razem trwa to minutę-dwie, bo pobierają się pakiety.

## 3. Uruchom bazę danych

Program potrzebuje PostgreSQL. Nie instaluj go — wystarczy kontener.
Docker Desktop musi być uruchomiony, a potem, w PowerShellu:

```powershell
docker run --name firmapro-baza -d `
  -e POSTGRES_PASSWORD=postgres `
  -e "POSTGRES_INITDB_ARGS=--encoding=UTF8 --locale-provider=icu --icu-locale=pl-PL --locale=C.UTF-8" `
  -p 5432:5432 -v firmapro-dane:/var/lib/postgresql/data postgres:16
```

To samo zrobisz z VS Code: **Ctrl+Shift+P** → `Tasks: Run Task` →
**baza: uruchom**.

Bazę uruchamiasz raz. Po ponownym włączeniu komputera wystarczy ją wznowić:

```powershell
docker start firmapro-baza
```

> **Dane przeżywają wyłączenie komputera**, bo leżą na woluminie
> `firmapro-dane`. Żeby zacząć od zupełnie czystej bazy:
> `docker rm -f firmapro-baza; docker volume rm firmapro-dane`
> i uruchom kontener od nowa.

## 4. Uruchom program

W VS Code naciśnij **F5**.

Program się zbuduje, wystartuje i sam otworzy przeglądarkę na
<http://localhost:5017>. Zaloguj się kontem demonstracyjnym:

```
demo@firmapro.pl / demo1234
```

Konto i przykładowe dane zakładają się same przy pierwszym uruchomieniu —
ale **tylko w trybie deweloperskim**. Na serwerze nie powstają, bo hasło jest
wypisane w kodzie źródłowym.

**Zatrzymanie:** czerwony kwadrat na pasku debugowania albo **Shift+F5**.

> **Skąd program wie, gdzie jest baza?** Z ustawień domyślnych —
> `localhost:5432`, użytkownik `postgres`, hasło `postgres`. Dokładnie takie
> ustawia polecenie z poprzedniego kroku, więc nie ma tu nic do wpisywania.

## 5. Uruchom testy

```powershell
dotnet test
```

Albo w VS Code: **Ctrl+Shift+P** → `Tasks: Run Task` → **testy**. Panel
*Testing* (ikona kolby na lewym pasku) pozwala uruchomić pojedynczy test
i wejść w niego debugerem.

Testy potrzebują tej samej bazy co program — każdy przebieg zakłada własną,
tymczasową i kasuje ją po sobie. Nie ruszają danych, na których pracujesz.

**Zanim cokolwiek wypchniesz, testy muszą przechodzić.** Jest ich ponad
czterysta i to one pilnują rzeczy, których nie widać na ekranie: sum VAT,
izolacji danych między firmami, poprawności pliku dla KSeF.

---

## Jak wygląda praca

**Zmiana wyglądu strony.** Pliki `.cshtml` w `src/FirmaPro.Web/Pages/`.
Po zapisaniu odśwież przeglądarkę — nie trzeba nic uruchamiać od nowa.

**Zmiana kodu w C#.** Trzeba zatrzymać program (**Shift+F5**) i uruchomić
ponownie (**F5**).

**Zmiana w bazie danych.** Po dopisaniu pola do encji w
`src/FirmaPro.Dane/Encje/Encje.cs` trzeba wygenerować migrację:

```powershell
dotnet tool install --global dotnet-ef    # raz, przy pierwszym użyciu
dotnet ef migrations add NazwaZmiany --project src/FirmaPro.Dane --startup-project src/FirmaPro.Dane --output-dir Migracje
```

Migracja wykona się sama przy najbliższym uruchomieniu programu. **Zajrzyj
do wygenerowanego pliku, zanim go zapiszesz** — to on decyduje, co stanie się
z danymi już istniejącymi w bazie klienta.

**Zapisanie zmian:**

```powershell
git add -A
git commit -m "Opis tego, co się zmieniło i dlaczego"
git push
```

---

## Zasady przyjęte w tym kodzie

Warto je znać, bo kompilator ich pilnuje i inaczej nie zbudujesz programu.

**Ostrzeżenia są błędami.** `TreatWarningsAsErrors` jest włączone —
niewykorzystana zmienna czy brakujący komentarz przy publicznej metodzie
zatrzymują budowanie. To męczy przez pierwszy tydzień i oszczędza czas przez
resztę życia programu.

**Wszystko po polsku** — nazwy klas, metod, zmiennych i komentarze. Kod ma
się czytać tym samym językiem, którym mówi się o fakturach.

**Kwoty to zawsze `decimal`, nigdy `double`.** Typy zmiennoprzecinkowe nie
zapisują dokładnie liczby 0,10 — przy podatkach kończy się to groszem, który
nie chce się zgodzić.

**Warstwa dziedziny nie zna bazy danych ani ekranów.** Reguły podatkowe
siedzą w `FirmaPro.Domena` i nie wolno im zależeć od niczego innego.

**Dane firm są rozdzielone filtrem w kontekście bazy.** Nowa tabela z danymi
klienta musi implementować `INalezyDoFirmy` — jest test, który to sprawdza.

---

## Gdy coś nie działa

**„dotnet: nie rozpoznano polecenia"** — PowerShell był otwarty przed
instalacją SDK. Zamknij go i otwórz od nowa.

**F5 nic nie robi albo pyta o wybór debugera** — nie zainstalowało się
rozszerzenie C# Dev Kit. **Ctrl+Shift+X**, wpisz `C# Dev Kit`, zainstaluj
i uruchom VS Code ponownie.

**Program startuje i od razu się kończy z błędem o połączeniu z bazą** —
kontener nie działa. Sprawdź `docker ps`; jeśli go nie ma, `docker start
firmapro-baza`.

**„port 5432 already in use"** — masz już gdzieś uruchomiony PostgreSQL.
Albo go zatrzymaj, albo zmień port kontenera na `-p 5433:5432` i wskaż go
programowi zmienną `FIRMAPRO_DB`.

**Testy nie przechodzą zaraz po pobraniu kodu** — najczęściej też brak bazy.
Wszystkie 400+ powinno przechodzić na świeżo pobranej gałęzi; jeśli nie,
napisz, co dokładnie się wysypało.
