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
| Zakładanie firm, zapraszanie współpracowników, role | gotowe |
| Własne konto, zmiana i odzyskiwanie hasła | gotowe |
| Wysyłka poczty (zaproszenia, hasła, faktury) | gotowe |
| Wysyłka faktury e-mailem do kontrahenta | gotowe |
| Płatności, należności i przypomnienia | gotowe |
| Kartoteka kontrahentów | gotowe |
| Wystawianie faktur i podgląd dokumentu | gotowe |
| Faktury korygujące (także korekta do korekty) | gotowe |
| Faktury zaliczkowe i końcowe | gotowe |
| Duplikat faktury | gotowe |
| Ustawienia firmy wraz z tokenem KSeF | gotowe |
| Wizualizacja PDF z kodem QR | gotowe |
| Faktury zakupu | gotowe |
| Pobieranie faktur zakupu z KSeF | gotowe, **niesprawdzone na żywym KSeF** |
| Rejestr VAT sprzedaży i zakupów | gotowe |
| Deklaracja i plik JPK_V7M | gotowe, **układ pliku niesprawdzony schematem** |
| Wysyłka JPK do urzędu | poza zakresem — plik składa się aplikacją MF |
| Wdrożenie (kontenery, HTTPS, pierwsze konto) | gotowe |
| Kopie zapasowe ze sprawdzanym odtwarzaniem | gotowe |

## Uruchomienie

Wymagany **.NET SDK 10.0** lub nowszy oraz PostgreSQL.

```bash
git clone https://github.com/jedrzejrakowski/firma-PRO.git
cd firma-PRO

docker run -d --name firmapro-db -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:16

export FIRMAPRO_DB="Host=localhost;Port=5432;Database=firmapro;Username=postgres;Password=postgres"
dotnet run --project src/FirmaPro.Web
```

Program sam zakłada bazę i wykonuje migracje. Przy pracy nad programem
(`ASPNETCORE_ENVIRONMENT=Development`) tworzy też firmę demonstracyjną
z kontem `demo@firmapro.pl` i hasłem `demo1234`, żeby dało się od razu wejść
i zobaczyć działający system.

Konto demonstracyjne ma hasło wypisane w kodzie źródłowym, więc **poza trybem
deweloperskim nie powstaje w ogóle** - na serwerze byłoby otwartymi drzwiami
do ksiąg firmy. Do uruchomienia na serwerze służy [wdrożenie](#wdrożenie),
które zakłada zamiast niego konto właściciela z hasłem podanym w ustawieniach.

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

### Korekta do korekty i osobna seria

Korektę można wystawić także **do korekty** - drugą poprawkę tej samej faktury
robi się właśnie tak, bo poprawia się ostatni obowiązujący stan dokumentu.

Korekty mają **własną serię numeracji** (`KOR/{ROK}/{MC}/{NR}`), niezależną
od ciągu faktur sprzedaży. Przepisy tego nie wymagają, ale przy przeglądaniu
rejestru widać od razu, który dokument jest poprawką.

## Duplikat faktury

Gdy odbiorca zgubi egzemplarz, wystawia się duplikat (art. 106l ustawy):
ta sama treść, dopisane oznaczenie „DUPLIKAT" i data jego wydania. W programie
to przycisk **Duplikat** przy fakturze - nie powstaje nowy dokument w bazie
ani nie idzie nic do KSeF, bo duplikat nie jest osobną fakturą.

## Faktury zaliczkowe i końcowe

Faktura zaliczkowa dokumentuje **pieniądze, które wpłynęły**, a nie dostawę.
Dlatego formularz jest inny niż przy zwykłej fakturze: pozycje opisują
**zamówienie**, a osobne pole to kwota wpłaty brutto.

- podatek liczony jest **„w stu"** - zaliczka jest kwotą brutto,
- kwota dzieli się między stawki zamówienia **proporcjonalnie** do ich
  wartości; grosz z zaokrąglenia dopisywany jest do największej części,
  żeby suma części zawsze równała się wpłacie,
- zaliczka wyższa niż wartość zamówienia jest odrzucana,
- w pliku FA(3) idzie sekcja `Zamowienie` z pozycjami i `WartoscZamowienia`.

Zaliczka jest z definicji zapłacona, więc program **zapisuje przy niej wpłatę**
na pełną kwotę - nie sam znacznik. Dzięki temu ekran zapłaty, należności
i wydruk mówią to samo.

### Co robi faktura końcowa

Faktura końcowa obejmuje **całą dostawę**, ale wykazuje ją pomniejszoną
o zafakturowane wcześniej zaliczki (art. 106f ust. 3 ustawy). W programie
oznacza to trzy rzeczy naraz:

1. **Plik FA(3)** niesie sekcje `FakturaZaliczkowa` wskazujące rozliczane
   zaliczki - numerem KSeF, a gdy zaliczka poszła poza systemem, własnym
   numerem ze znacznikiem `NrKSeFZN`.
2. **Do zapłaty** - na ekranie i na wydruku - to wartość dostawy **minus**
   zaliczki. Zaliczka wchodzi jako wpłata przy fakturze końcowej, więc
   należności pokazują samą dopłatę. Bez tego nabywca dostałby dokument
   z żądaniem zapłaty za coś, co już opłacił.
3. **Rejestr VAT** odejmuje zaliczki od faktury końcowej, stawka po stawce.
   Podatek od zaliczki wykazano już w miesiącu jej otrzymania - gdyby końcowa
   weszła całą wartością dostawy, ta sama sprzedaż trafiłaby do podstawy
   opodatkowania dwa razy.

Jedną zaliczkę można rozliczyć **tylko raz**. Pilnuje tego sprawdzenie przed
zapisem i - na wypadek dwóch żądań naraz - warunek jednoznaczności w bazie.

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

### Pobieranie zakupów z KSeF

Faktur zakupu nie trzeba przepisywać - program potrafi wciągnąć je prosto
z KSeF (`Zakupy → Pobierz z KSeF`). Pyta o zakres dat, pokazuje wszystko,
co system ma na Twoją firmę jako nabywcę, i przenosi do rejestru dokumenty,
które zaznaczysz.

**Import nie jest automatyczny i celowo taki nie będzie.** KSeF wie, ile
faktura kosztowała, ale nie wie dwóch rzeczy, które decydują o podatku: czy
zakup jest środkiem trwałym i czy w ogóle przysługuje od niego odliczenie.
To rozstrzygnięcia nabywcy, więc program pokazuje listę i pyta, zamiast
zgadywać.

Reszta dzieje się sama:

- **datą wpływu** jest dzień przyjęcia faktury przez KSeF - od tej chwili
  nabywca ma do niej dostęp, więc to ona wyznacza najwcześniejszy możliwy
  okres odliczenia (art. 86 ust. 10b pkt 1). Okres widać na liście jeszcze
  przed pobraniem;
- **żadna faktura nie wejdzie do rejestru dwa razy** - decyduje o tym numer
  KSeF, pilnowany więzem unikalności w bazie, a nie samą kontrolą w kodzie.
  Dokument już wpisany zostaje na liście, ale oznaczony; ukrycie go kazałoby
  zgadywać, czy czegoś nie brakuje;
- **kwoty pochodzą wyłącznie z KSeF**, nie z formularza w przeglądarce.
  Zaznaczenie wiersza mówi tylko „weź tę fakturę".

Metadane z KSeF niosą kwoty netto i VAT łącznie, bez rozbicia na stawki -
i to wystarcza. Zakupy wykazuje się w rejestrze i w JPK_V7 sumami, a nie
w podziale na stawki, w odróżnieniu od sprzedaży.

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
8. **Wdrożenie** — kontenery, HTTPS, kopie zapasowe ✔
9. **Konta i role** — zakładanie firm, zapraszanie, uprawnienia ✔
10. **Hasła i poczta** — własne konto, odzyskiwanie hasła ✔
11. **Wysyłka faktur do kontrahenta** — PDF pocztą wprost z programu ✔
12. **Płatności i należności** — wpłaty, przeterminowania, przypomnienia ✔
13. **Braki w fakturowaniu** — zaliczkowe i końcowe, duplikat, osobna seria korekt ✔
14. Dalej: zestawienia dla księgowej, magazyn, KPiR

## Konta, firmy i role

Jedno konto może pracować w wielu firmach - tak pracują biura rachunkowe.
Firmę wybiera się w pasku u góry, a nie w adresie strony: identyfikator firmy
siedzi w ciasteczku logowania, więc podmiana czegokolwiek w adresie nie
otworzy cudzych ksiąg.

### Trzy role

| Rola | Co może |
|---|---|
| **Podgląd** | tylko odczyt - nie zapisze niczego, nawet znając adres formularza |
| **Księgowy** | faktury, zakupy, kartoteki, rejestry i JPK |
| **Właściciel** | wszystko, w tym ustawienia firmy (a więc i token KSeF) oraz rozdawanie dostępu |

Rola pilnowana jest w dwóch miejscach, bo ukrycie przycisku zabezpieczeniem
nie jest. Ekrany właściciela wskazane są przy rejestracji stron w
`Program.cs` - nie da się do nich wejść wpisaniem adresu. Rolę podglądu
pilnuje osobny filtr, który odrzuca **każde** żądanie zmieniające dane;
reguła jest zamykająca, więc nowy ekran jest zablokowany od pierwszego dnia,
a nie dopiero wtedy, gdy ktoś pomyśli o dopisaniu go do listy. Wyjątki są
trzy i zmianą danych firmy nie są: zalogowanie, wylogowanie i przejście do
innej firmy.

Firma nie może zostać bez właściciela: ostatniemu nie da się ani odebrać
dostępu, ani obniżyć roli. Firma, do której nikt nie ma pełnych praw, byłaby
firmą nie do naprawienia od środka.

### Zapraszanie współpracowników

Program nie wysyła poczty, więc zaproszenie ma postać **jednorazowego
odnośnika**: właściciel wystawia je na ekranie „Dostęp" i przekazuje, jak mu
wygodnie. Odnośnik jest wart tyle, co hasło, dlatego:

- zawiera 32 bajty losowości - nie da się go zgadnąć,
- traci ważność po 7 dniach,
- działa **raz**, a „zużycie" go to jedno polecenie z warunkiem, że nikt nie
  był szybszy. Dwie osoby, które klikną ten sam odnośnik w tej samej chwili,
  nie wejdą obie,
- wystawienie nowego zaproszenia dla tego samego adresu unieważnia poprzednie
  - inaczej po zmianie roli w obiegu byłyby dwa odnośniki dające różne
  uprawnienia.

Zaproszenie na adres, który ma już konto, wymaga **hasła do tego konta**.
Bez tego wystawienie zaproszenia na cudzy adres byłoby sposobem na przejęcie
konta razem ze wszystkimi firmami, do których należy.

### Hasła i własne konto

Każdy zalogowany ma ekran **Moje konto**: zmiana hasła, imię i nazwisko oraz
lista firm, w których pracuje. Rola tego nie ogranicza — mówi, co wolno robić
w firmie, a nie czy wolno zmienić własne hasło.

**Zmiana hasła zamyka wszystkie sesje**, także tę, z której ją zrobiono.
Przy koncie trzymany jest stempel bezpieczeństwa: zmienia się przy każdej
zmianie hasła, siedzi w ciasteczku logowania i jest sprawdzany przy każdym
żądaniu. Bez tego zmiana hasła nie dawałaby nic komuś, komu wykradziono
ciasteczko — a hasło zmienia się zwykle właśnie dlatego, że coś wyciekło.

Zapomniane hasło da się odzyskać na dwa sposoby:

- **pocztą** — ekran „Nie pamiętam hasła" wysyła jednorazowy odnośnik ważny
  dwie godziny. Odpowiedź jest zawsze taka sama,
  niezależnie od tego, czy konto istnieje: inaczej ekran stałby się sposobem
  na sprawdzanie, kto ma tu konto;
- **przez właściciela firmy** — na ekranie „Dostęp" wystawia współpracownikowi
  odnośnik do ustawienia nowego hasła. Nie poznaje przy tym cudzego hasła:
  ustawia je sam zainteresowany. To jedyna droga w instalacji bez poczty,
  więc ekran „Nie pamiętam hasła" mówi wtedy wprost, żeby się o nią zwrócić.

### Poczta

Poczta jest **opcjonalna**. Bez niej program działa w całości, tylko
zaproszenia i odnośniki do zmiany hasła przekazuje się samemu — właściciel
widzi je na ekranie „Dostęp". Ustawia się ją w `.env` wdrożenia
(`POCZTA_SERWER` i dalsze).

Ekrany pytają, czy poczta działa, zanim obiecają wysyłkę: komunikat
„wysłaliśmy wiadomość", po którym nic nie przychodzi, jest gorszy niż
uczciwe „poczta nie jest skonfigurowana". Odnośnik pokazywany jest zawsze,
także gdy wiadomość poszła — bo bywa zatrzymywana przez filtry.

### Zakładanie firm przez stronę

Ekran rejestracji jest **domyślnie wyłączony**. Instalacja postawiona dla
jednej firmy nie powinna pozwalać obcym zakładać w niej kont - byłby to
najprostszy sposób na zajrzenie do środka. Włącza go `REJESTRACJA_OTWARTA`
w pliku `.env` wdrożenia, gdy program ma obsługiwać wiele firm.

Przy pracy nad programem rejestracja jest otwarta, żeby dało się wyklikać
całą drogę bez zaglądania do ustawień.

## Wysyłka faktury kontrahentowi

Kontrahent dostaje z KSeF plik XML, ale chce czegoś, co da się przeczytać
i podpiąć pod przelew. Z ekranu faktury idzie więc pocztą **ta sama
wizualizacja PDF**, którą można pobrać ręcznie — z kodem QR, jeśli faktura
jest już w KSeF. Plik XML dołącza się na życzenie, jednym polem wyboru.

Adres podpowiadany jest z kartoteki kontrahenta, ale zostaje do poprawienia:
faktury bywają wysyłane do księgowości klienta, a nie na adres z kartoteki.

Każda wysyłka zostawia ślad widoczny przy fakturze — data, adres i to, co
poszło w załączniku. Ślad jest osobną listą, a nie jednym polem „wysłano",
bo faktura bywa wysyłana kilka razy: pod poprawiony adres albo drugi raz,
gdy pierwsza wiadomość zaginęła. Przy sporze o to, czy kontrahent fakturę
dostał, liczy się cała historia.

Wysyłka do kontrahenta to co innego niż wysyłka do KSeF: pierwsza jest
grzecznością wobec klienta i można ją powtórzyć, druga wprowadza fakturę do
obiegu prawnego i zdarza się raz.

## Płatności i należności

Program pokazywał dotąd, co zostało wystawione, ale nie to, kto jest firmie
winien pieniądze. Ekran **Należności** odpowiada na to drugie pytanie:
niezapłacone faktury ułożone według terminu zapłaty (a nie daty wystawienia),
suma do odzyskania i osobno suma po terminie.

Wpłaty zapisuje się przy fakturze. Są **osobną listą, a nie polem
„zapłacono"**: należność bywa regulowana w ratach, a przy sporze liczy się,
kiedy i ile wpłynęło. Pola `Zaplacono` i `DataZaplaty` przy fakturze zostają
jako podsumowanie - to one trafiają na wydruk i do pliku FA(3) - i program
utrzymuje je sam na podstawie wpłat, żeby nie rozjechały się z prawdą.

Reguła rozliczania siedzi w warstwie dziedziny (`Rozliczenie`), bo decyduje
o pieniądzach, a nie o wyglądzie ekranu: ta sama liczba pokazuje się przy
fakturze, na liście należności i w treści przypomnienia.

Dwie rzeczy, które łatwo zrobić źle:

- **zapłata w ostatnim dniu terminu jest zapłatą w terminie** - opóźnienie
  zaczyna się nazajutrz. Inaczej program wysyłałby wezwania ludziom, którzy
  zapłacili zgodnie z umową;
- **nadpłata nie jest błędem i nie znika** - kontrahent bywa, że zaokrągli
  przelew w górę albo zapłaci dwa razy. To pieniądze do zwrotu albo do
  rozliczenia z następną fakturą, więc program je pokazuje.

Przypomnienie o zapłacie wysyła się z ekranu faktury: niesie kwotę pozostałą
do zapłaty, liczbę dni po terminie i samą fakturę w załączniku. Faktura już
zapłacona przypomnienia nie dostanie - program tego pilnuje, bo taka pomyłka
kosztuje więcej niż niewysłane wezwanie.

## Wdrożenie

Na serwerze program uruchamia się jako cztery kontenery: baza danych,
program, pośrednik podający HTTPS i usługa kopii zapasowych. Wszystko stoi
w katalogu `wdrozenie/`:

```bash
cd wdrozenie
cp .env.przyklad .env      # uzupełnij hasła, adres i dane pierwszego konta
chmod 600 .env
docker compose up -d
```

Certyfikat HTTPS pobiera pośrednik (Caddy) z Let's Encrypt i odnawia go sam;
dla nazwy `localhost` wystawia certyfikat lokalny. Port programu **nie jest
wystawiony na zewnątrz** - z sieci widać wyłącznie pośrednika. Ma to
znaczenie: program ufa nagłówkom `X-Forwarded-*`, bo dochodzą do niego tylko
od pośrednika. Wystawienie portu 8080 wprost do internetu pozwoliłoby
podszyć się pod szyfrowane połączenie.

Stan programu pokazuje adres `/zdrowie` - odpowiada `sprawny`, gdy program
ma połączenie z bazą. To pod ten adres warto podpiąć zewnętrzny nadzór.

### Trzy rzeczy, które łatwo przeoczyć

**Klucze ochrony muszą przeżyć wymianę kontenera.** Tym samym kluczem
zaszyfrowany jest token KSeF. Gdyby klucze powstawały od nowa przy każdym
starcie, po pierwszej aktualizacji obrazu token przestałby się odczytywać,
a program zgłosiłby zwyczajny „brak tokena" - nikt nie skojarzyłby przyczyny
ze skutkiem. Dlatego katalog kluczy leży na woluminie, a poza trybem
deweloperskim program **nie wstanie**, dopóki nie zostanie wskazany
(`Aplikacja__KatalogKluczy`).

Same klucze leżą na woluminie niezaszyfrowane - chroni je wyłącznie system
plików serwera. Kto ma dostęp do woluminu, ma dostęp do tokena KSeF. Warto
więc trzymać dysk zaszyfrowany i pilnować, kto ma konto na serwerze.

**Pierwsze konto bierze się z ustawień wdrożenia**, nie z formularza:
`KONTO_EMAIL`, `KONTO_HASLO`, `FIRMA_NAZWA` i `FIRMA_NIP` w pliku `.env`.
Konto powstaje wyłącznie wtedy, gdy w bazie nie ma jeszcze żadnego
użytkownika - kolejne uruchomienia nic już nie zmieniają, więc zmiana hasła
w programie nie zostanie cofnięta przy restarcie. Hasło krótsze niż 12 znaków
zatrzymuje start programu.

**Plik `.env` nie trafia do repozytorium.** Są w nim hasła do bazy i do konta
właściciela. W repozytorium leży tylko `.env.przyklad`.

### Kopie zapasowe

Utrata bazy to jedyna awaria w tym programie, której nie da się cofnąć -
w środku są księgi podatkowe, a nie dane, które da się odtworzyć z pamięci.
Usługa `kopie` robi więc zrzut bazy co dobę (domyślnie; zmienia to
`KOPIE_CO_ILE_GODZIN`) i trzyma 14 pokoleń.

Najważniejsze jest jednak to, co dzieje się po zrzucie: **każda kopia jest
od razu odtwarzana do bazy pomocniczej i sprawdzana.** Kopia, której nigdy
nie odtworzono, nie jest kopią zapasową, tylko plikiem w nadziei. Sprawdzane
jest, czy:

- `pg_restore` odtwarza plik bez błędu - to wyłapuje zrzut ucięty w połowie
  przez brak miejsca albo zerwane połączenie,
- odtworzona baza ma **ten sam zestaw tabel** co pracująca - inaczej kopia
  wgra się na nowy serwer, ale program jej nie zrozumie,
- tabele `firmy` i `uzytkownicy` **nie są puste** - to wyłapuje najgroźniejszy
  przypadek, w którym kopiowana jest nie ta baza, co trzeba: plik jest, waży
  swoje, a w środku nic.

Kopia, która nie przeszła sprawdzenia, dostaje przyrostek `.NIESPRAWDZONA`,
nie liczy się jako pokolenie i nigdy nie zostanie skasowana przy sprzątaniu.
Przebieg każdej rundy zapisywany jest w `dziennik.txt` na woluminie kopii.

Kopia doraźna, na przykład przed aktualizacją:

```bash
docker compose run --rm kopie bash /skrypty/kopia.sh raz
```

Sprawdzenie konkretnego pliku bez ruszania bazy:

```bash
docker compose run --rm kopie bash /skrypty/kopia.sh sprawdz firmapro-20260811-0300.dump
```

### Odtworzenie bazy z kopii

```bash
docker compose stop program
docker compose run --rm kopie bash /skrypty/odtworz.sh          # lista kopii
docker compose run --rm kopie bash /skrypty/odtworz.sh firmapro-20260811-0300.dump
docker compose start program
```

Skrypt pyta o potwierdzenie nazwą bazy i **zanim cokolwiek skasuje, zapisuje
obecny stan** do pliku `przed-odtworzeniem-*.dump`. Gdyby odtwarzana kopia
okazała się nie tą, o którą chodziło, jest dokąd wrócić.

### Aktualizacja programu

```bash
git pull
cd wdrozenie
docker compose run --rm kopie bash /skrypty/kopia.sh raz   # najpierw kopia
docker compose up -d --build
```

Migracje bazy wykonują się przy starcie programu. Kopia przed aktualizacją
jest tu istotna: migracji nie da się cofnąć jednym poleceniem.

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
formularzem, zakładają firmę, zapraszają współpracownika, wystawiają fakturę,
pobierają jej XML, zapisują ustawienia i pobierają zakupy z KSeF przez
wygenerowany formularz, przechodząc tę samą drogę co użytkownik.

Uprawnienia sprawdzane są od strony napastnika, a nie widoku: testy nie
patrzą, czy przycisk zniknął, tylko wysyłają żądania wprost pod adresy -
tak jak zrobiłby ktoś, kto zna adres i chce go użyć mimo braku uprawnień.
Sprawdzane jest też, że zablokowany zapis naprawdę się nie odbył, a nie
tylko że przeglądarka dostała przekierowanie. Pobieranie zakupów sprawdzane jest łącznie z tym, co
naprawdę wychodzi z przeglądarki - zaznaczone pole wyboru wysyła dwie
wartości, a odznaczone samo „false" z ukrytego pola; właśnie w tej kolejności
siedziała kiedyś usterka przepuszczająca zakup do odliczenia wbrew woli
użytkownika. Powód jest praktyczny — błąd
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

Pobieranie zakupów z KSeF sprawdzone jest atrapą serwera - odwzorowanie pól,
wyznaczanie okresu odliczenia i ochrona przed dwukrotnym wpisaniem faktury.
Nie sprawdzono natomiast, czy **prawdziwe** metadane z KSeF mają dokładnie
te nazwy pól, których spodziewa się klient; wynikają one ze specyfikacji
OpenAPI, nie z odpowiedzi żywego serwera.

Z wdrożenia sprawdzone jest to, co dało się sprawdzić w środowisku budowy:
obraz programu buduje się i startuje w trybie produkcyjnym, zakłada pierwsze
konto z ustawień, nie zakłada danych demonstracyjnych, a zapisany token KSeF
**przeżywa podmianę kontenera** (sprawdzone przez wymianę kontenera przy tym
samym woluminie kluczy). Kopie zapasowe sprawdzone są na działającej bazie:
udana runda, wykrycie kopii uciętej w połowie, wykrycie kopii pustej oraz
odtworzenie bazy nadpisanej „przez pomyłkę".

Wysyłka poczty sprawdzona jest **przeciwko prawdziwemu serwerowi SMTP** -
prostemu serwerowi uruchomionemu na czas próby. Wiadomość dotarła w całości:
nagłówki, polska treść i dwa załączniki, z których plik PDF zaczyna się od
`%PDF-`, a XML niesie przestrzeń nazw wzoru FA(3). Nie sprawdzono natomiast
połączenia **szyfrowanego z uwierzytelnieniem** - do tego trzeba prawdziwego
dostawcy poczty. Pierwsze wysłanie faktury u siebie warto potraktować jako
sprawdzenie ustawień serwera poczty.

Nie udało się natomiast uruchomić **całego zestawu z docker-compose naraz** -
w środowisku budowy zablokowane jest pobieranie obrazów `postgres` i `caddy`
z Docker Hub. Sam plik `docker-compose.yml` przechodzi sprawdzenie składni
(`docker compose config`), ale pierwsze `docker compose up` u siebie warto
potraktować jako pierwsze prawdziwe uruchomienie - i od razu zajrzeć do
`dziennik.txt` na woluminie kopii, czy pierwsza kopia się odtworzyła.

Kod QR na wydruku został odczytany z gotowego pliku PDF czytnikiem kodów
i porównany ze skrótem SHA-256 wysłanego dokumentu — link prowadzi dokładnie
tam, gdzie powinien. Nie sprawdzono natomiast, czy strona KSeF pod tym
adresem pokaże fakturę; to znów wymaga połączenia z żywym systemem.

Faktury zaliczkowe i końcowe przeszły drogę **przez przeglądarkę**: wystawienie
zaliczki, rozliczenie jej fakturą końcową, nieudaną próbę rozliczenia drugi
raz, duplikat i dwie korekty jedna po drugiej. Sprawdzone są też skutki
liczbowe - kwota do zapłaty na ekranie, w należnościach i na wydruku PDF oraz
wpis w rejestrze VAT pomniejszony o zaliczkę. Same pliki FA(3) w trzech nowych
wariantach przechodzą walidację oryginalnym schematem Ministerstwa.
