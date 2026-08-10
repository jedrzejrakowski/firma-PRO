# Firma PRO

System do fakturowania i księgowości dla małych firm, budowany jako aplikacja
przeglądarkowa z myślą o obsłudze wielu firm w jednej instalacji.

Faktury powstają w strukturze **FA(3)** — wzorze obowiązującym w Krajowym
Systemie e-Faktur od 1 lutego 2026 r.

> **Stan prac: program działa od przeglądarki do gotowego dokumentu.** Można
> się zalogować, prowadzić kartotekę kontrahentów, wystawić fakturę, obejrzeć
> ją, pobrać plik FA(3) oraz wydruk PDF dla kontrahenta, wprowadzić faktury
> zakupu, zobaczyć rejestr VAT i deklarację JPK_V7 za wybrany okres oraz
> pobrać gotowy plik. Wysyłka do KSeF jest
> zaimplementowana, ale nie została jeszcze potwierdzona połączeniem z żywym
> systemem — patrz [Czego jeszcze nie sprawdzono](#czego-jeszcze-nie-sprawdzono).

---

## Co już działa

| Element | Stan |
|---|---|
| Model dziedziny (faktura, pozycje, stawki, podsumowanie) | gotowe |
| Wyliczanie sum w rozbiciu na pola FA(3) | gotowe |
| Walidacja przed wysyłką (NIP, NRB, daty, limity schematu) | gotowe |
| Generator XML FA(3) | gotowe |
| Warstwa danych (EF Core, PostgreSQL, wielofirmowość) | gotowe |
| Komunikacja z KSeF (wysyłka, UPO, faktury zakupowe) | gotowe |
| Link weryfikacyjny kodu QR (KOD I) | gotowe |
| Logowanie i konta użytkowników | gotowe |
| Kartoteka kontrahentów | gotowe |
| Wystawianie faktur i podgląd dokumentu | gotowe |
| Faktury korygujące | gotowe |
| Ustawienia firmy wraz z tokenem KSeF | gotowe |
| Wizualizacja PDF z kodem QR | gotowe |
| Faktury zakupu | gotowe |
| Rejestr VAT sprzedaży i zakupów | gotowe |
| Deklaracja i plik JPK_V7M | gotowe, **układ pliku niesprawdzony schematem** |
| Wysyłka JPK do urzędu | poza zakresem — plik składa się aplikacją MF |

## Uruchomienie

Wymagany **.NET SDK 10.0** lub nowszy oraz PostgreSQL.

```bash
git clone https://github.com/jedrzejrakowski/firma-PRO.git
cd firma-PRO

docker run -d --name firmapro-db -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:16

export FIRMAPRO_DB="Host=localhost;Port=5432;Database=firmapro;Username=postgres;Password=postgres"
dotnet run --project src/FirmaPro.Web
```

Program sam zakłada bazę i wykonuje migracje. Przy pierwszym uruchomieniu
tworzy też firmę demonstracyjną z kontem `demo@firmapro.pl` i hasłem
`demo1234`, żeby dało się od razu wejść i zobaczyć działający system.
**Przed udostępnieniem programu komukolwiek trzeba założyć własne konto
i usunąć demonstracyjne.**

### Testy

```bash
dotnet test
```

Testy nie wymagają internetu ani tokena KSeF, ale potrzebują serwera bazy —
wskazuje go zmienna `FIRMAPRO_TEST_DB`. Każdy przebieg zakłada własną bazę
i kasuje ją po sobie.

## Budowa rozwiązania

```
src/
├── FirmaPro.Domena/     model faktury, stawki VAT, walidacja - bez zależności
├── FirmaPro.Ksef/       generator XML FA(3), kryptografia, klient API
├── FirmaPro.Dane/       encje, kontekst EF Core, migracje, izolacja firm
├── FirmaPro.Jpk/        deklaracja i plik JPK_V7M
├── FirmaPro.Wydruk/     wizualizacja faktury w PDF wraz z kodem QR
└── FirmaPro.Web/        aplikacja przeglądarkowa (ASP.NET Core, Razor Pages)
testy/
└── FirmaPro.Testy/      testy jednostkowe, bazodanowe i całej aplikacji
schematy/                schemat FA(3) opublikowany przez Ministerstwo Finansów
```

Warstwa dziedziny nie zależy od bazy danych ani od interfejsu. To celowe:
reguły podatkowe zmieniają się z innego powodu i w innym rytmie niż ekrany
czy sposób przechowywania danych, więc trzymamy je osobno.

Strony nie znają ani generatora XML, ani klienta KSeF — sięgają po nie przez
usługi w `FirmaPro.Web/Uslugi`. Dzięki temu zmiana wyglądu ekranu nie może
zepsuć treści dokumentu wysyłanego do urzędu.

## Decyzje, które warto znać

**Kwoty są typu `decimal`, nigdy `double`.** Typy zmiennoprzecinkowe nie
potrafią dokładnie zapisać nawet 0,10 zł, a KSeF odrzuca faktury, na których
sumy nie zgadzają się co do grosza.

**Zaokrąglanie zawsze przez `Kwoty.Zaokraglij`.** Domyślne `Math.Round`
w .NET stosuje zaokrąglanie bankierskie (do najbliższej parzystej), które
w podatkach daje złe wyniki — potrzebne jest „w górę od połowy".

**Podatek liczony od sumy netto w danej stawce**, a nie jako suma podatków
z pozycji. Przy trzech pozycjach po 0,10 zł daje to 0,07 zł, a nie 0,06 zł.
Tej kolejności działań wymaga art. 106e ustawy o VAT i walidacja po stronie
KSeF.

**Stawka podatku to nie liczba.** Obok „23" i „8" schemat dopuszcza „0 WDT",
„zw", „oo" czy „np I", a każda z nich trafia do innego pola podsumowania.
Typ `StawkaVat` trzyma stawkę razem z jej przyporządkowaniem, żeby nie dało
się dodać nowej i zapomnieć, gdzie ma być wykazana.

## Pułapki schematu FA(3)

Rzeczy, które wychwyciła dopiero walidacja prawdziwym schematem — warto
o nich pamiętać przy zmianach w generatorze:

- pola podsumowania `P_13_1`..`P_13_5` oraz `P_14_1`..`P_14_4` są
  **obowiązkowe zawsze**, także z wartością `0.00`,
- znaczniki `JST` i `GV` po stronie nabywcy są w FA(3) **obowiązkowe** — to
  nowość względem FA(2),
- sekcja `Adnotacje` wymaga jawnego **zaprzeczenia** okoliczności, które nie
  wystąpiły (`P_19N`, `P_22N`, `P_PMarzyN`),
- `P_1` to sama data, mimo że typ w schemacie nazywa się `TDataT`,
- kwoty nie mogą mieć notacji wykładniczej ani zer wiodących.

## Schematy

Katalog `schematy/` zawiera oryginalny plik `schemat_FA(3)_v1-0E.xsd`
opublikowany przez Ministerstwo Finansów oraz
`StrukturyDanych_v10-0E_lokalny.xsd` — lokalny odpowiednik schematu typów
wspólnych, który FA(3) importuje z serwera `crd.gov.pl`.

Odpowiednik zawiera wyłącznie te typy, których FA(3) faktycznie używa, i służy
tylko do uruchamiania testów bez dostępu do internetu. Kilka typów jest w nim
celowo szerszych niż w oryginale, więc walidacja lokalna może przepuścić
dokument, który KSeF odrzuci — nigdy odwrotnie. **Źródłem prawdy pozostaje
schemat opublikowany przez Ministerstwo Finansów.**

## Izolacja danych między firmami

System obsługuje wiele firm w jednej instalacji, więc wyciek dokumentów
jednej firmy do drugiej byłby katastrofą. Izolacja jest wymuszona na trzy
niezależne sposoby:

1. **globalne filtry zapytań** — każde zapytanie o dane firmowe jest
   automatycznie zawężone; nie da się tego pominąć zapominając o warunku,
2. **automatyczne ustawianie firmy** przy dodawaniu rekordu — nie trzeba
   o tym pamiętać, więc nie da się zapomnieć,
3. **kontrola przy zapisie** — próba dopisania, zmiany lub przeniesienia
   rekordu należącego do innej firmy kończy się wyjątkiem.

Gdy firma nie jest wybrana, filtry nie przepuszczają niczego. Brak informacji
o firmie oznacza „nic nie widać", a nie „widać wszystko".

Osobno pilnowana jest **niezmienność wystawionych dokumentów**: po wysłaniu
faktury do KSeF nie da się zmienić jej treści — zapisywalne pozostają tylko
pola dotyczące obiegu w KSeF i rozliczenia płatności. Błąd koryguje się
fakturą korygującą, tak jak wymagają tego przepisy.

## Komunikacja z KSeF

Obsługiwaną metodą uwierzytelniania jest **token KSeF** — generuje się go raz
w aplikacji webowej KSeF i od tego momentu system działa bez udziału podpisu
kwalifikowanego. Druga dopuszczalna metoda (podpis XAdES) wymaga certyfikatu
kwalifikowanego i nie jest zaimplementowana.

Faktury przesyłane są zgodnie z wymaganiami systemu: każda sesja dostaje nowy
klucz **AES-256-CBC**, a ten klucz szyfrowany jest **RSA-OAEP (SHA-256)**
certyfikatem Ministerstwa Finansów **pobieranym z API**, a nie zaszytym
w kodzie — dzięki temu program przetrwa rotację certyfikatów.

Cała komunikacja schowana jest za interfejsem `IKlientKsef`. Reszta systemu
nigdy nie widzi konkretnej implementacji, więc podmiana sposobu rozmowy
z KSeF — na przykład na bibliotekę wydawaną przez Ministerstwo Finansów —
nie wymaga zmian w warstwie aplikacji.

Środowisko (testowe, demo, produkcyjne) jest ustawieniem **firmy**, a nie
całej instalacji: jedna firma może dopiero sprawdzać integrację, gdy druga
wystawia już faktury produkcyjne. Klient powstaje więc na żądanie, przez
`IFabrykaKlientowKsef`, dla środowiska tej firmy, której dotyczy wysyłka.

### Token KSeF

Token pozwala wystawiać faktury w imieniu firmy — to najbardziej wrażliwa
dana w całym systemie. Traktowany jest odpowiednio:

- w bazie leży **zaszyfrowany** (`IOchronaTokena`), nigdy otwartym tekstem,
- **nigdy nie wraca na stronę** — formularz ustawień pokazuje wyłącznie to,
  czy token jest zapisany; puste pole oznacza „zostaw jak było", więc zapis
  zmiany adresu nie odcina firmy od KSeF,
- nie trafia do dziennika zdarzeń ani do komunikatów o błędach,
- zmienna środowiskowa `KSEF_TOKEN` ma pierwszeństwo przed wartością
  z bazy — pozwala pracować, nie zapisując sekretu w ogóle.

Docelowo, przy wdrożeniu produkcyjnym, klucze ochrony powinny trafić do
zewnętrznego magazynu sekretów; zmienia się wtedy tylko implementacja
`IOchronaTokena`.

## Wydruk faktury

Kontrahent dostaje z KSeF plik XML, ale chce czegoś, co da się przeczytać
i podpiąć pod przelew. Program składa więc wizualizację w PDF: dane obu
stron, tabelę pozycji, podsumowanie w rozbiciu na stawki, kwotę do zapłaty
wraz z zapisem słownym oraz kod QR do weryfikacji dokumentu.

**Kod QR trafia na wydruk tylko wtedy, gdy faktura naprawdę jest w KSeF.**
Powstaje z zapisanego skrótu przesłanego pliku, a nie z dokumentu składanego
na nowo — plik XML niesie znacznik czasu wytworzenia, więc wygenerowany
ponownie miałby inny skrót i kod prowadziłby do dokumentu, którego system nie
zna. Dokument jeszcze niewysłany drukuje się z wyraźnym ostrzeżeniem
**PROJEKT**, bo faktura zaczyna istnieć w obrocie prawnym dopiero po
przyjęciu przez KSeF.

Sam kod rysowany jest wektorowo, prostokąt po prostokącie, a nie wklejany
jako obrazek. Dzięki temu pozostaje ostry przy każdej rozdzielczości drukarki
— a to ostrość krawędzi decyduje o tym, czy telefon go odczyta.

Wydruk korzysta z dwóch bibliotek, obu na licencji **MIT**: `PDFsharp`
składa dokument, `QRCoder` wylicza siatkę kodu. Licencja miała tu znaczenie:
popularny QuestPDF jest wygodniejszy w użyciu, ale jego bezpłatna licencja
przestaje obowiązywać po przekroczeniu progu przychodu firmy, a to zły
fundament pod program, który ma być sprzedawany.

Krój pisma (Liberation Sans, licencja SIL OFL) jest osadzony w bibliotece —
patrz `src/FirmaPro.Wydruk/Czcionki/LICENCJA.md`. Gdyby brać go z systemu,
ten sam dokument wyglądałby inaczej na serwerze i na komputerze księgowej,
a przy braku czcionki wydruk sypałby się dopiero u klienta.

## Faktury korygujące

Faktura przyjęta przez KSeF jest niezmienna, więc korekta to jedyny sposób
poprawienia błędu. Korektę wystawia się z poziomu faktury pierwotnej -
formularz startuje od jej pozycji, a użytkownik poprawia to, co się zmieniło.

**Dokument niesie obie wersje pozycji**: sprzed zmiany (znacznik `StanPrzed`
ze schematu) i po niej. Do rejestru VAT oraz do pliku wysyłanego do KSeF trafia
**różnica** między nimi. Gdyby korekta wykazywała nowy stan, rejestr policzyłby
tę samą sprzedaż drugi raz, a podatek należny wyszedłby niemal podwójny.

Wydruk pokazuje obie tabele obok siebie oraz dane faktury korygowanej
i przyczynę korekty - tego wymaga art. 106j, a odbiorca i tak chce wiedzieć,
co właściwie się zmieniło.

### Okres, w którym korekta ma skutek

Wybór przy wystawianiu decyduje o tym, do którego miesiąca trafi korekta:

- **na bieżąco** (rabat, zwrot towaru) - okres wystawienia korekty,
- **wstecz** (błąd istniejący od początku) - okres faktury pierwotnej,
  co zwykle oznacza korektę złożonej już deklaracji.

Program zapisuje ten wybór jako datę ujęcia w rejestrze, więc korekta wstecz
naprawdę wraca do właściwego miesiąca - a nie tylko do pola w pliku XML.

Korekta przechodzi **walidację prawdziwym schematem FA(3)** we wszystkich
wariantach: faktury z numerem KSeF i sprzed KSeF, korekty zbiorczej do kilku
faktur naraz oraz każdego z trzech typów skutku.

Czego jeszcze nie ma: **korekty do korekty** (program mówi o tym wprost,
zamiast wystawić dokument, którego nikt nie sprawdził) i osobnej serii
numeracji dla korekt - na razie idą tą samą serią co faktury, co jest zgodne
z przepisami, ale różni się od przyzwyczajeń części księgowych.

## Rejestr VAT

Rejestr nie jest osobno przechowywany - powstaje z faktur przy każdym otwarciu
ekranu. Dzięki temu poprawka w dokumencie widać od razu w rejestrze i nie ma
dwóch źródeł prawdy, które mogłyby się rozjechać.

### Data na fakturze rzadko decyduje o okresie

To najczęstsze źródło pomyłek, więc reguły siedzą w osobnym miejscu
(`TerminyVat`) i mają własne testy:

- **sprzedaż** trafia do okresu, w którym wykonano usługę lub dostarczono
  towar (art. 19a ust. 1), a nie w którym wystawiono fakturę. Usługa wykonana
  28 sierpnia, zafakturowana 3 września, należy do sierpnia;
- **zakup** można odliczyć najwcześniej w okresie, w którym u sprzedawcy
  powstał obowiązek podatkowy, ale nie wcześniej niż po otrzymaniu faktury
  (art. 86 ust. 10 i 10b pkt 1);
- kto nie odliczył od razu, ma na to jeszcze trzy okresy przy rozliczeniu
  miesięcznym albo dwa przy kwartalnym (art. 86 ust. 11). Późniejsze
  odliczenie wymaga już korekty wstecz - program to sprawdza i nie pozwala
  zapisać dokumentu z datą spoza terminu.

Reguły są ogólne i obejmują typową sprzedaż oraz typowy zakup. Przy mediach,
najmie, zaliczkach czy metodzie kasowej obowiązek podatkowy powstaje inaczej -
dlatego okres da się wskazać ręcznie. **Program nie próbuje rozpoznać wyjątku
sam**: zła podpowiedź w podatkach jest gorsza niż jej brak.

### Co pokazuje ekran

Sprzedaż w rozbiciu na stawki, zakupy z podziałem na środki trwałe
i pozostałe nabycia (tego podziału wymaga JPK_V7) oraz różnicę podatku
za okres. Nadwyżka przeniesiona z poprzednich miesięcy **nie** jest tu
doliczana - należy do deklaracji, nie do rejestru, i tam zostanie
uwzględniona.

Zakup służący sprzedaży zwolnionej albo celom prywatnym można oznaczyć jako
nieodliczany. Zostaje wtedy w rejestrze jako dokument, ale nie wchodzi ani
do podatku naliczonego, ani do kwot wykazywanych w deklaracji.

## JPK_V7

Deklaracja powstaje z tego samego rejestru, który widać na ekranie — nie ma
osobnego miejsca, w którym dałoby się ją „poprawić" niezależnie od dokumentów.

### Dwie reguły zaokrąglania w jednym pliku

Część **deklaracyjna** podawana jest w pełnych złotych (art. 63 § 1 Ordynacji
podatkowej), część **ewidencyjna** — w groszach. Zaokrąglana jest suma w każdym
polu, a nie każdy dokument z osobna; pola podsumowujące (P_38, P_48) liczone są
z już zaokrąglonych pól składowych, żeby deklaracja zgadzała się sama ze sobą.

### Nadwyżka między okresami

Deklaracja za dany miesiąc potrzebuje nadwyżki z miesiąca poprzedniego.
Program mógłby ją wyliczać w łańcuchu wstecz, ale wtedy **poprawka w starej
fakturze po cichu zmieniałaby deklaracje już złożone w urzędzie**. Dlatego
kwota utrwalana jest przy zamknięciu okresu i od tego momentu się nie zmienia.
Dopóki poprzedni okres nie jest zamknięty, program przyjmuje zero — zamiast
podpowiadać liczbę, której nikt nie zatwierdził.

### Czego program nie robi

**Nie wysyła pliku do urzędu.** Złożenie JPK wymaga podpisu kwalifikowanego,
profilu zaufanego albo danych autoryzujących i odbywa się przez bramkę
Ministerstwa. Plik pobiera się z programu i składa bezpłatną aplikacją
*Klient JPK_WEB* — która przy okazji sprawdzi go schematem.

Deklaracja obejmuje sprzedaż i zakupy krajowe. Transakcje, których system
jeszcze nie zbiera — wewnątrzwspólnotowe nabycie, import usług, ulga na złe
długi, korekty środków trwałych — mają w deklaracji własne pola i wymagają
osobnego uzupełnienia. Sprzedaż w stawce, która nie ma odpowiednika w JPK_V7
(ryczałt dla taksówek, rozliczany deklaracją VAT-12), nie znika po cichu:
program pokazuje ostrzeżenie.

### Czego nie udało się sprawdzić

**Układ pliku nie został potwierdzony oficjalnym schematem XSD.** W środowisku,
w którym powstawał ten kod, serwisy Ministerstwa Finansów są niedostępne, więc
nazwy i kolejność elementów pochodzą z dokumentacji struktury, a nie z samego
schematu. Przy fakturach FA(3) walidacja prawdziwym schematem wychwyciła błędy
nie do przewidzenia — tutaj takiego zabezpieczenia zabrakło.

Miejsca, którym warto przyjrzeć się najpierw:

- numer pola „nadwyżka do przeniesienia na następny okres" (przyjęto P_62),
- nazwy i kolejność elementów `SprzedazWiersz` i `ZakupWiersz`,
- oznaczenia nowe w strukturze obowiązującej od lutego 2026 r. (`NrKSeF`),
- przestrzeń nazw i atrybuty `KodFormularza`.

Błąd w którymkolwiek z tych miejsc **wychodzi głośno** — walidator odrzuci plik
przy pierwszej próbie. Groźniejsze byłyby błędne kwoty, więc to one są obłożone
testami.

**Jak to potwierdzić:** pobierz schemat JPK_V7M ze strony Ministerstwa Finansów
(Struktury JPK), zapisz go w katalogu `schematy/` pod nazwą zaczynającą się od
`Schemat_JPK_V7M` i uruchom `dotnet test`. Test walidujący plik schematem
uruchomi się wtedy sam. Dopóki pliku nie ma, zgłasza się jako **pominięty** —
brak sprawdzenia nie ma wyglądać jak sprawdzenie.

## Plan

1. **Fundament** — model, walidacja, generator FA(3) ✔
2. **Warstwa danych** — EF Core i PostgreSQL, wielofirmowość, migracje ✔
3. **Integracja z KSeF** — uwierzytelnianie, sesja, wysyłka, UPO, zakupy ✔
4. **Interfejs webowy** — konta, kontrahenci, wystawianie faktur, ustawienia ✔
5. **Wizualizacja PDF** — wydruk faktury z kodem QR ✔
6. **Rejestr VAT** — sprzedaż, zakupy i rozliczenie okresu ✔
7. **JPK_V7** — deklaracja i plik do złożenia ✔ (do potwierdzenia schematem)
8. Dalej: magazyn, KPiR, CRM

## Uwaga o testach

Zgodność generowanego dokumentu ze wzorem sprawdzana jest **prawdziwym
schematem XSD**, w wariantach obejmujących nabywcę bez NIP-u, kontrahenta
z numerem VAT UE, sprzedaż zwolnioną, odwrotne obciążenie, WDT i eksport.

Komunikacja z KSeF sprawdzana jest atrapą serwera, która **naprawdę
odszyfrowuje** przesyłkę: rozszyfrowuje token kluczem prywatnym, odtwarza
klucz sesji, odszyfrowuje fakturę i porównuje ją bajt w bajt z oryginałem
wraz ze skrótami SHA-256. Dzięki temu test wykrywa błąd w kopercie
kryptograficznej, a nie tylko literówkę w nazwie pola.

Izolacja firm sprawdzana jest na **prawdziwym PostgreSQL**, a nie na bazie
w pamięci. Filtry zapytań, więzy unikalności i typ `numeric` zachowują się
inaczej w każdym silniku — testowanie ich na atrapie dawałoby złudne poczucie
bezpieczeństwa akurat tam, gdzie pomyłka byłaby najdroższa.

Aplikacja webowa sprawdzana jest **uruchomiona w całości**: testy logują się
formularzem, wystawiają fakturę, pobierają jej XML i zapisują ustawienia,
przechodząc tę samą drogę co użytkownik. Powód jest praktyczny — błąd
w konfiguracji usług potrafi wywrócić stronę mimo bezbłędnej kompilacji,
a właśnie taki błąd zdarzył się przy tworzeniu klienta KSeF.

## Czego jeszcze nie sprawdzono

**Połączenia z żywym KSeF nie da się zweryfikować w środowisku budowy** —
dostęp do `ksef.mf.gov.pl` jest tam zablokowany. Sprawdzenie należy wykonać
u siebie, przeciwko bezpłatnemu środowisku testowemu Ministerstwa: zapisać
token w Ustawieniach, wystawić fakturę i wysłać ją.

Zweryfikowana jest natomiast **zawartość** przesyłki: dokument przechodzi
walidację oryginalnym schematem XSD, a koperta kryptograficzna — test
z atrapą serwera, która naprawdę odszyfrowuje przesłane dane.

Plik JPK_V7 ma sprawdzone kwoty i zgodność sum kontrolnych z zawartością
ewidencji, ale **nie jego układ** — patrz [JPK_V7](#jpk_v7).

Kod QR na wydruku został odczytany z gotowego pliku PDF czytnikiem kodów
i porównany ze skrótem SHA-256 wysłanego dokumentu — link prowadzi dokładnie
tam, gdzie powinien. Nie sprawdzono natomiast, czy strona KSeF pod tym
adresem pokaże fakturę; to znów wymaga połączenia z żywym systemem.
