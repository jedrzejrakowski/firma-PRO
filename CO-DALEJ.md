# Co dalej

Notatka dla kolejnej sesji pracy nad programem. Opisuje stan na dziś, rzeczy
nierozstrzygnięte i kolejność, w jakiej warto brać następne funkcje.

Gałąź robocza: `claude/excel-invoicing-ksef-koxjud`.

---

## Co działa

Księga przychodów i rozchodów oraz ewidencja przychodów przy ryczałcie:
wybór formy opodatkowania, kwalifikacja kosztów do kolumn, sumy narastające,
zapisy ręczne bez faktury, wydruk księgi i plik CSV dla biura rachunkowego.

Ewidencja środków trwałych z planem odpisów (liniowa, degresywna z przejściem
na liniową, jednorazowa), limit kosztu przy samochodach osobowych, likwidacja
zamykająca plan bez kasowania historii. Odpisy wchodzą do kolumny 13 same.

Spis z natury: arkusze remanentowe z czterema sposobami wyceny (§ 26
rozporządzenia), zamknięcie arkusza, dobór remanentu początkowego i końcowego
dla roku oraz roczne rozliczenie dochodu różnicą remanentów.

Zaliczka na podatek dochodowy: skala z kwotą wolną i progiem, podatek liniowy,
ryczałt z odliczeniami dzielonymi między stawki, rozliczenie miesięczne albo
kwartalne, odliczenie składek i straty z lat ubiegłych, zaokrąglenie do złotych
i terminy z przypomnieniem w menu.

ZUS przedsiębiorcy: cztery tytuły ubezpieczenia (ulga na start, preferencyjny,
Mały ZUS Plus liczony z dochodu roku poprzedniego, pełny), składka zdrowotna
osobnym wzorem dla skali, liniowego i ryczałtu, Fundusz Pracy zależny
od podstawy, terminy przesuwane na dzień roboczy, przypomnienie w menu,
roczne rozliczenie zdrowotnej i wpisywanie zapłaconych składek do kolumny 13.

Wystawianie faktur (zwykłe, korygujące, zaliczkowe, końcowe, duplikaty),
wysyłka do KSeF z pobraniem UPO, faktury cykliczne, cennik, kontrahenci,
płatności i należności, rejestr VAT, deklaracja JPK\_V7, import faktur zakupu
z KSeF, zestawienia dla biura rachunkowego, faktury walutowe z kursem NBP,
wyszukiwarka, konta i role, kopie zapasowe, uruchomienie w kontenerach.

Struktura FA(3) jest pokryta po stronie podmiotów w całości: `Podmiot1`,
`Podmiot2`, `Podmiot3` (dziesięć ról plus rola własna), `PodmiotUpowazniony`
(komornik, organ egzekucyjny, przedstawiciel podatkowy) oraz `Podmiot1K`
i `Podmiot2K` (korekta danych sprzedawcy i nabywcy).

Jedno ograniczenie zapisane świadomie: schemat dopuszcza korygowanie danych
wielu nabywców naraz (`Podmiot2K` do stu jednu wystąpień). Model, generator
i czytnik to obsługują, ale **baza i formularz zapisują jednego** - nabywcę
z faktury. Korekta danych dodatkowego nabywcy z sekcji `Podmiot3` to przypadek
na tyle rzadki, że nie warto pod niego budować osobnej tabeli; gdyby okazał
się potrzebny, dołożenie jej nie ruszy niczego poza warstwą zapisu.

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
- **Kwoty ZUS na rok bieżący trzeba potwierdzić.** Minimalne wynagrodzenie,
  prognozowane przeciętne i progi zmieniają się co roku i siedzą
  w `StawkiZus.Wpisane`. Rok 2026 jest oznaczony jako niepotwierdzony i program
  mówi o tym na ekranie; rok, którego nie zna, nie liczy się wcale.
  **Do rozstrzygnięcia:** czy stawki mają być edytowalne w programie. Dziś są
  w kodzie, bo są krajowe, a baza jest wielofirmowa - tabela per firma
  znaczyłaby poprawianie tych samych liczb w każdej firmie osobno.
- **Dochód w księdze nie jest podstawą opodatkowania** - kolumna 14 nie
  obejmuje zakupu towarów, więc dochód miesięczny w księdze różni się od
  rocznego. Pełny rachunek jest na ekranie „Spis z natury". Napisane jest
  to i na ekranie, i na wydruku.
- **Zaliczka na PIT nie zna ulg.** Program nie liczy ulgi na dzieci,
  rehabilitacyjnej, IP Box, wspólnego rozliczenia z małżonkiem ani daniny
  solidarnościowej. Ekran mówi to wprost - to wyliczenie, nie deklaracja.
- **Podstawa zaliczki to nie różnica kolumn 9 i 14.** Kolumna 14 nie obejmuje
  zakupu towarów, więc podatek liczy się przez `RozliczenieRoczne.Zbuduj`,
  które te kolumny uwzględnia i koryguje o remanenty. Ta sama droga służy
  podstawie składki zdrowotnej.
- **Faktury sprzedaży sprzed wprowadzenia `XmlWyslany`** drukują się ze starej
  drogi (model z bazy). To zamierzone zachowanie zapasowe, nie usterka.

---

## Kolejność następnych funkcji

### 1. Przebudowa pulpitu

Awansuje na pierwsze miejsce, bo powód, dla którego czekała, właśnie zniknął:
program liczy już VAT, ZUS i PIT, więc kafelki mogą pokazywać kwoty
wyliczone, a nie zgadywane.

Uzgodniona zawartość: VAT z terminem, kalendarz terminów, przychody kontra
koszty na jednym wykresie, struktura należności (wykres pierścieniowy),
najwięksi dłużnicy, ostatnie koszty, szybkie przyciski - a teraz także
kafelki ZUS i PIT z najbliższym terminem.

### 2. Kadry i płace

**Kilkakrotnie większe niż wszystko dotąd zbudowane razem.** Umowy, listy płac,
PIT-4R, PIT-11, zgłoszenia do ZUS, urlopy, zwolnienia. To nie jest funkcja,
tylko drugi program obok tego. Wymaga osobnej rozmowy o zakresie, zanim padnie
pierwsza linijka kodu.

### 3. Zeznanie roczne

PIT-36, PIT-36L albo PIT-28. Program ma już wszystkie składniki - dochód
z remanentami, składki, zapłacone zaliczki - ale zeznanie to osobny dokument
z własnym schematem i własnymi ulgami. Do rozmowy o zakresie.

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
