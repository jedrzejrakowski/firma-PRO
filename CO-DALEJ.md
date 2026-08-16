# Co dalej

Notatka dla kolejnej sesji pracy nad programem. Opisuje stan na dziś, rzeczy
nierozstrzygnięte i kolejność, w jakiej warto brać następne funkcje.

Gałąź robocza: `claude/excel-invoicing-ksef-koxjud`.

---

## Co działa

Wystawianie faktur (zwykłe, korygujące, zaliczkowe, końcowe, duplikaty),
wysyłka do KSeF z pobraniem UPO, faktury cykliczne, cennik, kontrahenci,
płatności i należności, rejestr VAT, deklaracja JPK\_V7, import faktur zakupu
z KSeF, zestawienia dla biura rachunkowego, faktury walutowe z kursem NBP,
wyszukiwarka, konta i role, kopie zapasowe, uruchomienie w kontenerach.

Struktura FA(3) jest pokryta po stronie podmiotów w całości: `Podmiot1`,
`Podmiot2`, `Podmiot3` (dziesięć ról plus rola własna), `PodmiotUpowazniony`
(komornik, organ egzekucyjny, przedstawiciel podatkowy) oraz `Podmiot1K`
(korekta danych sprzedawcy).

Dwie wizualizacje, obie potrzebne i obie zostają:

- **wydruk firmowy** (`WydrukFaktury`) — idzie do kontrahenta, ma kod QR
  i kwotę do zapłaty rzucającą się w oczy,
- **wizualizacja KSeF** (`WydrukKsef`) — urzędowy widok dokumentu, ten sam
  układ co u każdego dostawcy; idzie do akt i do biura rachunkowego.

Obie powstają z **odczytu pliku XML** przesłanego do KSeF (`Fa3Czytnik`), a nie
z wierszy bazy — dla faktur sprzedaży z kolumny `XmlWyslany`, dla zakupowych
z `XmlKsef` pobranego przy imporcie.

---

## Czego nie sprawdzono na żywym KSeF

To jedyne miejsca, gdzie program może się jeszcze zachować inaczej, niż zakłada
kod. Wszystko poniżej działa przeciwko atrapie i przechodzi testy, ale nie
zostało uruchomione przeciwko prawdziwemu systemowi:

- uwierzytelnienie **certyfikatem** (token działa),
- pobranie **UPO** po zamknięciu sesji,
- pobranie **XML faktury zakupowej** przy imporcie,
- odpowiedzi środowiska produkcyjnego na dokumenty z nowymi sekcjami
  (`Podmiot3`, `PodmiotUpowazniony`, `Podmiot1K`).

Do przejścia w środowisku testowym Ministerstwa, gdy będzie token.

## Otwarte drobiazgi

- **JPK\_V7 nie jest sprawdzany schematem MF.** Test jest napisany, ale
  pominięty (`Skip`), bo w repozytorium nie ma pliku XSD — trzeba go pobrać ze
  stron Ministerstwa i położyć obok schematów FA(3) w `schematy/`.
- **Faktury zakupu sprzed wprowadzenia `XmlKsef`** nie mają zapisanego pliku,
  więc nie da się dla nich zrobić wizualizacji. Ponowny import tego samego
  okresu uzupełnia braki — duplikaty są pomijane.
- **Faktury sprzedaży sprzed wprowadzenia `XmlWyslany`** drukują się ze starej
  drogi (model z bazy). To zamierzone zachowanie zapasowe, nie usterka.

---

## Kolejność następnych funkcji

### 1. KPiR — księga przychodów i rozchodów

Duża rzecz, na osobną sesję od początku. Dotyka pięciu warstw: model kolumn
księgi, kwalifikacja dokumentów (sprzedaż i zakupy już są w bazie), ewidencja
z numeracją ciągłą, wydruk księgi za okres i podsumowania roczne.

Rzecz do rozstrzygnięcia na starcie: **KPiR nie liczy tego samego co rejestr
VAT.** Do księgi wchodzą kwoty netto, ale nie wszystkie — koszty
niestanowiące kosztu uzyskania przychodu wypadają, a niektóre przychody
(np. zwroty) wchodzą ze znakiem ujemnym. Nie da się jej zbudować jako widoku
rejestru VAT i próba pójścia na skróty tutaj kończy się księgą, która nie
zgadza się z zeznaniem rocznym.

### 2. ZUS

Składki właściciela, terminy, przypomnienia. Mniejsze niż KPiR, ale wymaga
KPiR-u przed sobą przy uldze na start i małym ZUS-ie.

### 3. Kadry i płace

**Kilkakrotnie większe niż wszystko dotąd zbudowane razem.** Umowy, listy płac,
PIT-4R, PIT-11, zgłoszenia do ZUS, urlopy, zwolnienia. To nie jest funkcja,
tylko drugi program obok tego. Wymaga osobnej rozmowy o zakresie, zanim padnie
pierwsza linijka kodu.

### 4. Przebudowa pulpitu — świadomie na koniec

Ustalone z właścicielem: pulpit przebudowujemy **po** KPiR, ZUS-ie i kadrach,
żeby nie robić tego od nowa przy każdej nowej funkcji.

Uzgodniona zawartość: VAT z terminem, kalendarz terminów, przychody kontra
koszty na jednym wykresie, struktura należności (wykres pierścieniowy),
najwięksi dłużnicy, ostatnie koszty, szybkie przyciski.

**Bez kafelków PIT i ZUS, dopóki program ich nie liczy.** Kafelek pokazujący
kwotę, której nikt nie wyliczył, jest gorszy niż jego brak.

---

## Zasady, które obowiązują w tym repozytorium

- Kod, komentarze, nazwy i interfejs **po polsku**.
- Komentarz tłumaczy **dlaczego**, nie co — a przy przepisach podaje artykuł.
- Każda funkcja dotykająca podatku ma test, który sprawdza regułę z ustawy,
  a nie samo działanie kodu.
- Dokumenty wysłane do KSeF są niezmienne — pilnuje tego `FirmaProDbContext`.
- Tokenów i haseł nie ma w repozytorium ani w dzienniku; token KSeF jest
  szyfrowany przez `IOchronaTokena`.
- Przed zakończeniem pracy: `dotnet test` w całości, potem commit i push
  na gałąź roboczą.
