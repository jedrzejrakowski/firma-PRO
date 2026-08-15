# Pokaz w internecie — Firma PRO na małym serwerze

Ta instrukcja jest po to, żeby **inne osoby mogły wejść do programu
przeglądarką**, ze swojego komputera albo telefonu, bez instalowania
czegokolwiek. Wystarczy im link.

Nie trzeba znać się na serwerach. Wszystkie polecenia są do przepisania
jeden do jednego.

**Czas:** około 40 minut, z czego połowa to czekanie.
**Koszt:** kilkadziesiąt złotych miesięcznie za serwer plus ewentualnie
15–60 zł rocznie za nazwę domeny. Serwer rozlicza się godzinowo i można go
skasować zaraz po pokazie — dwa dni to wtedy kilka złotych.

---

## Co powstanie

Program stanie na małym serwerze w chmurze i będzie dostępny pod adresem
w rodzaju `https://firmapro.twojadomena.pl`. Certyfikat HTTPS załatwia się
sam — nie ma tu nic do klikania.

Współpracownicy będą **zakładać własne konta**. Każdy dostaje wtedy własną
firmę i własny, czysty program: wystawia swoje faktury, widzi swój rejestr
VAT i nie miesza nikomu innemu. To najlepszy układ do oceny wyglądu
i wygody.

---

## 1. Wykup serwer

**Nie sugeruj się nazwami pakietów — patrz na trzy liczby:**

| Parametr | Ile potrzeba | Uwaga |
|---|---|---|
| Rdzenie (vCPU) | 2 | „współdzielone" (*shared*) w zupełności wystarczą |
| Pamięć (RAM) | 4 GB | 2 GB też zadziała, ale budowanie programu będzie się wlokło |
| Dysk | 40 GB | zajmiemy około 5 GB |

To najmniejsza półka u każdego dostawcy. **Dedykowanych rdzeni nie
kupuj** — kosztują kilka razy więcej i na pokazie nic nie dadzą.

Gdzie:

- [Hetzner Cloud](https://www.hetzner.com/cloud) — rozliczanie godzinowe,
  więc kilkudniowy pokaz kosztuje grosze. Na stronie wybiera się najpierw
  rodzinę pakietów: **Cost-Optimized** albo **Regular Performance**;
  *General Purpose* to dedykowane rdzenie i jest tu niepotrzebny.
  W sierpniu 2026 parametrom z tabeli odpowiadał pakiet **CPX22**.
  Uwaga na pakiety o numerach kończących się jedynką (CPX11, CPX21) —
  to starsza seria, droższa za te same parametry.
- [OVHcloud](https://www.ovhcloud.com/pl/vps/) — ośrodek w Warszawie,
  polska faktura i obsługa, rozliczanie miesięczne.
- [e24cloud](https://www.e24cloud.com/) (Beyond.pl) — polska firma, ośrodek
  w Poznaniu, rozliczanie godzinowe jak u Hetznera.
- [Mikr.us](https://mikr.us/) — polski i najtańszy, ale pakiety bywają
  ciasne i **koniecznie sprawdź rodzaj wirtualizacji** (patrz niżej).

> **Zanim zapłacisz, sprawdź trzy rzeczy w opisie usługi:**
>
> 1. **Wirtualizacja KVM**, a nie OpenVZ ani LXC. Tańsze oferty bywają
>    kontenerowe i wtedy Docker albo nie ruszy, albo wymaga zabiegów,
>    których ta instrukcja nie opisuje.
> 2. **Pełny dostęp administratora** (root) — bez niego nie zainstalujesz
>    Dockera.
> 3. **Ubuntu 24.04** na liście systemów do wyboru.

> **Ceny sprawdź na stronie, nie tutaj.** Hetzner podnosił je w czerwcu
> 2026 i część pakietów bywa chwilowo niedostępna. Zwróć też uwagę, czy
> strona nie pokazuje cennika dla innego regionu — serwery w Stanach
> kosztują więcej niż te w Niemczech.

Przy zakładaniu serwera wybierz:

- **System:** Ubuntu 24.04 LTS.
- **Lokalizacja:** Niemcy albo Polska — im bliżej, tym szybciej działa
  i tym taniej.
- **Dostęp:** jeśli dostawca proponuje klucz SSH, a nie masz go jeszcze,
  wybierz logowanie hasłem. Do pokazu w zupełności wystarczy.

Po chwili dostaniesz **adres IP** serwera, na przykład `159.69.12.34`.
Zapisz go — będzie potrzebny dwa razy.

> **Nie zapomnij go skasować.** Serwer, o którym się zapomni, płaci sam
> przez rok. Ostatni rozdział tej instrukcji mówi, jak go usunąć.

## 2. Załatw nazwę, pod którą program będzie widoczny

Pod samym adresem IP program nie zadziała — certyfikat HTTPS wystawia się
dla nazwy, nie dla numeru.

**Jeśli masz już domenę** (choćby dla strony firmowej), wystarczy dopisać
do niej subdomenę — to nic nie kosztuje.

**Jeśli nie masz**, są dwie drogi:

- kupić domenę, np. w [OVH](https://www.ovhcloud.com/pl/domains/) albo
  [nazwa.pl](https://www.nazwa.pl/) — końcówki `.pl` bywają za 15–60 zł
  na pierwszy rok;
- wziąć darmową subdomenę w [DuckDNS](https://www.duckdns.org/) — logujesz
  się kontem Google, wpisujesz nazwę i adres IP serwera, i masz
  `cokolwiek.duckdns.org`. Do pokazu to zupełnie wystarczy i działa
  z certyfikatem HTTPS tak samo dobrze.

**W panelu domeny dodaj wpis:**

| Pole | Wartość |
|---|---|
| Typ | `A` |
| Nazwa | `firmapro` (albo `@`, jeśli chcesz całą domenę) |
| Wartość | adres IP serwera, np. `159.69.12.34` |

Zmiana rozchodzi się po świecie od kilku minut do godziny. Sprawdzisz ją
w PowerShellu na swoim komputerze:

```powershell
nslookup firmapro.twojadomena.pl
```

Gdy w odpowiedzi widać adres IP serwera — można iść dalej. **Nie zaczynaj
kroku 6, zanim to zadziała**: pośrednik poprosi wtedy o certyfikat dla
nazwy, która jeszcze nigdzie nie prowadzi, i dostanie odmowę.

## 3. Zaloguj się na serwer

Na Windows 11 nie trzeba nic instalować. Otwórz **PowerShell** i wpisz:

```powershell
ssh root@159.69.12.34
```

Przy pierwszym połączeniu zapyta `Are you sure you want to continue
connecting?` — wpisz `yes`. Potem podaj hasło z panelu dostawcy (przy
wpisywaniu hasła **nic się nie wyświetla**, nawet gwiazdki — to normalne).

Gdy pojawi się znak zachęty w rodzaju `root@ubuntu:~#`, jesteś na serwerze.
Od tej chwili wszystkie polecenia wpisujesz w tym oknie.

## 4. Zainstaluj Docker i zamknij niepotrzebne porty

```bash
curl -fsSL https://get.docker.com | sh
```

Trwa to dwie–trzy minuty. Potem zapora sieciowa — przepuszczamy wyłącznie
to, co potrzebne:

```bash
ufw allow 22/tcp && ufw allow 80/tcp && ufw allow 443/tcp && ufw --force enable
```

> Port 22 to Twoje logowanie na serwer, 80 i 443 to strona. Baza danych
> i sam program **nie mają wystawionego portu** — z internetu widać
> wyłącznie pośrednika podającego HTTPS.

## 5. Pobierz program

```bash
apt-get install -y git
git clone -b claude/excel-invoicing-ksef-koxjud \
  https://github.com/jedrzejrakowski/firma-PRO.git
cd firma-PRO/wdrozenie
```

> **Uwaga na gałąź.** Cała praca jest na gałęzi
> `claude/excel-invoicing-ksef-koxjud`, nie na głównej. Polecenie wyżej
> pobiera od razu właściwą.

## 6. Ustaw hasła i adres

Najpierw wygeneruj dwa długie hasła — do bazy i do swojego konta:

```bash
openssl rand -base64 24
openssl rand -base64 24
```

Skopiuj oba wyniki na bok. Teraz przygotuj plik z ustawieniami:

```bash
cp .env.przyklad .env
chmod 600 .env
nano .env
```

Otworzy się prosty edytor. Uzupełnij **pięć rzeczy**:

```ini
ADRES=firmapro.twojadomena.pl
HASLO_BAZY=<pierwsze wygenerowane hasło>

KONTO_EMAIL=twoj.adres@example.pl
KONTO_HASLO=<drugie wygenerowane hasło>
FIRMA_NAZWA=Firma Pokazowa sp. z o.o.
FIRMA_NIP=5252248481

# To jest ustawienie właściwe dla pokazu: każdy zakłada sobie własną firmę.
REJESTRACJA_OTWARTA=true
```

Zapisz: **Ctrl+O**, **Enter**, potem **Ctrl+X**.

> **Hasło konta musi mieć co najmniej 12 znaków** — przy krótszym program
> celowo nie wstanie. Wygenerowane wyżej ma ich 32.
>
> **Poczty nie ustawiaj.** Bez niej program działa normalnie; jedyne, czego
> nie zrobi, to wysyłka faktury e-mailem i odnośnika do zmiany hasła.
> Na pokaz to bez znaczenia.

## 7. Uruchom

```bash
docker compose up -d --build
```

Pierwsze uruchomienie **trwa 5–10 minut** — serwer pobiera narzędzia
i buduje program ze źródeł. Kolejne trwają kilkanaście sekund.

Sprawdź, czy wszystko wstało:

```bash
docker compose ps
```

Wszystkie cztery części mają być `running`, a `baza` dodatkowo `healthy`.

Potem sprawdź sam program:

```bash
curl -s https://firmapro.twojadomena.pl/zdrowie
```

Odpowiedź `sprawny` oznacza, że program ma połączenie z bazą, a certyfikat
HTTPS jest już wystawiony. Teraz wejdź na ten adres przeglądarką ze swojego
komputera — powinien pokazać się ekran logowania.

Zaloguj się danymi z `KONTO_EMAIL` i `KONTO_HASLO`.

---

## 8. Zaproś współpracowników

Wyślij im adres i krótką wiadomość. Poniższe można wkleić bez zmian:

> Cześć, wrzuciłem do internetu program do faktur, nad którym pracuję —
> chciałbym wiedzieć, jak Wam się podoba i czy da się z tego korzystać
> bez instrukcji.
>
> **https://firmapro.twojadomena.pl** → „Załóż firmę"
>
> Zakładasz własne konto i masz własny, pusty program — nikomu nie
> przeszkadzasz i niczego nie zepsujesz.
>
> Przy zakładaniu program prosi o NIP i sprawdza jego poprawność, więc
> wpisz jeden z tych do prób:
> `1111111111`, `2222222222`, `9876543210`, `5550001119`, `8112233446`
>
> **Dwie prośby:** nie wpisujcie prawdziwych danych klientów — to zwykły
> pokaz, nie miejsce na cudze dane. I nie przejmujcie się przyciskiem
> „Wyślij do KSeF": on wymaga osobnego klucza z Ministerstwa Finansów,
> więc powie, że go nie ma. Wszystko inne działa naprawdę.
>
> Najbardziej ciekawi mnie: czy widać od razu, co gdzie kliknąć, i czy
> wygląda to na program, którego chciałoby się używać.

**Co działa bez klucza do KSeF** (czyli wszystko, co ocenia się wzrokiem):
zakładanie kontrahentów, wystawianie faktur wszystkich rodzajów, PDF
z kodem QR, płatności i należności, rejestr VAT, deklaracja JPK_V7,
wyszukiwarka, pulpit.

**Co nie zadziała:** wysyłka faktury do KSeF i pobieranie faktur
kosztowych z KSeF. Jedno i drugie wymaga klucza wydawanego przez
Ministerstwo Finansów osobno dla każdego NIP-u.

---

## Trzy rzeczy, o których trzeba pamiętać

**Zostaw środowisko KSeF na „Testowe".** To ustawienie z ekranu
*Ustawienia firmy*. Na środowisku produkcyjnym każde kliknięcie „Wyślij do
KSeF" wystawia **prawdziwą fakturę** z prawdziwymi skutkami podatkowymi —
na pokazie nie ma to czego szukać. Świeżo założone konta i tak nie mają
żadnego klucza, więc niczego nie wyślą, ale swojego własnego konta pilnuj.

**Otwarta rejestracja znaczy otwarta dla wszystkich.** Każdy, kto pozna
adres, założy sobie konto. Na czas pokazu to zaleta, potem — niekoniecznie.
Zamyka się to jedną zmianą:

```bash
nano .env          # REJESTRACJA_OTWARTA=false
docker compose up -d
```

**Kopie zapasowe nie są tu do niczego potrzebne.** Robią się co dobę i tak,
ale to pokaz — nie ma w nim nic, czego szkoda by było stracić.

---

## Po pokazie

**Żeby zatrzymać, ale zachować dane:**

```bash
cd ~/firma-PRO/wdrozenie
docker compose down
```

**Żeby uruchomić z powrotem:**

```bash
docker compose up -d
```

**Żeby skasować wszystko razem z bazą:**

```bash
docker compose down -v
```

**Żeby przestać płacić** — usuń serwer w panelu dostawcy. Samo `docker
compose down` nie wystarczy, serwer nadal kosztuje. U Hetznera rozliczanie
jest godzinowe, więc dwa dni pokazu to kilkadziesiąt groszy.

---

## Gdy coś nie zadziała

**Przeglądarka nie otwiera strony w ogóle.** Najczęściej wpis w domenie
jeszcze się nie rozszedł. Sprawdź `nslookup nazwa` na swoim komputerze —
musi zwrócić adres IP serwera.

**Przeglądarka ostrzega o certyfikacie.** Pośrednik nie zdążył go pobrać
albo dostał odmowę, bo nazwa nie prowadzi do tego serwera. Zajrzyj do
dziennika:

```bash
docker compose logs posrednik | tail -30
```

**`baza is unhealthy`.** Baza nie wstała. Pokaż powód:

```bash
docker compose logs baza | tail -30
```

**Strona pokazuje błąd 502.** Program się jeszcze buduje albo nie wstał:

```bash
docker compose logs program | tail -40
```

**Nie pamiętam hasła do swojego konta.** Jest w pliku `.env` na serwerze
(`cat .env`) — pod warunkiem, że nie zmieniałeś go później w programie.
