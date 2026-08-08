# Firma PRO

System do fakturowania i księgowości dla małych firm, budowany jako aplikacja
przeglądarkowa z myślą o obsłudze wielu firm w jednej instalacji.

Faktury powstają w strukturze **FA(3)** — wzorze obowiązującym w Krajowym
Systemie e-Faktur od 1 lutego 2026 r.

> **Stan prac: fundament.** Gotowa jest warstwa dziedziny wraz z generatorem
> FA(3) i walidacją, sprawdzona oficjalnym schematem Ministerstwa Finansów.
> Warstwa danych, integracja z KSeF i interfejs webowy są w przygotowaniu —
> patrz [Plan](#plan).

---

## Co już działa

| Element | Stan |
|---|---|
| Model dziedziny (faktura, pozycje, stawki, podsumowanie) | gotowe |
| Wyliczanie sum w rozbiciu na pola FA(3) | gotowe |
| Walidacja przed wysyłką (NIP, NRB, daty, limity schematu) | gotowe |
| Generator XML FA(3) | gotowe, 57 testów |
| Warstwa danych (EF Core, PostgreSQL, wielofirmowość) | w przygotowaniu |
| Integracja z KSeF | w przygotowaniu |
| Interfejs webowy | w przygotowaniu |

## Uruchomienie

Wymagany **.NET SDK 10.0** lub nowszy.

```bash
git clone https://github.com/jedrzejrakowski/firma-PRO.git
cd firma-PRO
dotnet test
```

Testy nie wymagają bazy danych, internetu ani tokena KSeF.

## Budowa rozwiązania

```
src/
├── FirmaPro.Domena/     model faktury, stawki VAT, walidacja - bez zależności
└── FirmaPro.Ksef/       generator XML FA(3)
testy/
└── FirmaPro.Testy/      testy jednostkowe wraz z walidacją schematem XSD
schematy/                schemat FA(3) opublikowany przez Ministerstwo Finansów
```

Warstwa dziedziny nie zależy od bazy danych ani od interfejsu. To celowe:
reguły podatkowe zmieniają się z innego powodu i w innym rytmie niż ekrany
czy sposób przechowywania danych, więc trzymamy je osobno.

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

## Plan

1. **Fundament** — model, walidacja, generator FA(3) ✔
2. **Warstwa danych** — EF Core i PostgreSQL, wielofirmowość wymuszona
   globalnymi filtrami zapytań, migracje
3. **Integracja z KSeF** — uwierzytelnianie, sesja interaktywna, wysyłka, UPO,
   pobieranie faktur zakupowych
4. **Interfejs webowy** — konta, wybór firmy, kontrahenci, wystawianie faktur
5. **Rejestr VAT** — wynika niemal wprost z faktur i jest pomostem do JPK
6. **JPK_V7** — osobna integracja z Ministerstwem, z własnym uwierzytelnianiem
7. Dalej: magazyn, KPiR, CRM

## Uwaga o testach

Zgodność generowanego dokumentu ze wzorem sprawdzana jest **prawdziwym
schematem XSD**, w wariantach obejmujących nabywcę bez NIP-u, kontrahenta
z numerem VAT UE, sprzedaż zwolnioną, odwrotne obciążenie, WDT i eksport.

Połączenia z żywym KSeF nie da się zweryfikować w środowisku budowy — to
sprawdzenie należy wykonać u siebie, przeciwko bezpłatnemu środowisku
testowemu Ministerstwa.
