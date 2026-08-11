#!/bin/bash
# Kopie zapasowe bazy danych - wraz ze sprawdzeniem, czy dają się odtworzyć.
#
# Kopia, której nigdy nie odtworzono, nie jest kopią zapasową, tylko plikiem
# w nadziei. Dlatego po każdym zrzucie skrypt odtwarza go do bazy pomocniczej
# i sprawdza zawartość. Plik, który się nie odtworzył, nie liczy się jako
# pokolenie i nigdy nie zostanie skasowany po cichu przy sprzątaniu.
#
# Uruchamiany jest jako usługa "kopie" z docker-compose.yml. Poza pętlą
# obsługuje dwa polecenia doraźne:
#
#   kopia.sh raz               - jedna kopia i koniec (np. przed aktualizacją)
#   kopia.sh sprawdz <plik>    - odtworzenie wskazanej kopii bez zmian w bazie

set -euo pipefail

KATALOG="${KATALOG:-/kopie}"
DZIENNIK="$KATALOG/dziennik.txt"
CO_ILE_GODZIN="${CO_ILE_GODZIN:-24}"
ILE_POKOLEN="${ILE_POKOLEN:-14}"

# Tabele, które w działającym programie nie mają prawa być puste. Pusta
# kopia to najgroźniejszy przypadek: plik jest, waży swoje, a w środku nic.
WYMAGANE_TABELE=(firmy uzytkownicy)

zapisz() {
    printf '%s  %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$1" | tee -a "$DZIENNIK"
}

# Lista tabel w podanej bazie, posortowana - do porównania budowy.
tabele() {
    psql -d "$1" -Atc "
        SELECT table_name FROM information_schema.tables
        WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
        ORDER BY table_name"
}

liczba_wierszy() {
    psql -d "$1" -Atc "SELECT count(*) FROM \"$2\""
}

# Odtwarza kopię do bazy pomocniczej i sprawdza, co się odtworzyło.
# Baza pomocnicza znika niezależnie od wyniku.
sprawdz_kopie() {
    local plik="$1"
    local baza_probna="sprawdzenie_$(date '+%Y%m%d%H%M%S')_$$"
    local wynik=0

    createdb "$baza_probna"

    if ! pg_restore --dbname="$baza_probna" --exit-on-error --no-owner "$plik" \
         > /tmp/odtworzenie.log 2>&1; then
        zapisz "BŁĄD: odtworzenie kopii $(basename "$plik") nie powiodło się"
        sed 's/^/        /' /tmp/odtworzenie.log | tail -20 | tee -a "$DZIENNIK"
        dropdb --if-exists "$baza_probna"
        return 1
    fi

    # Budowa bazy musi się zgadzać - inaczej kopia odtworzy się na serwerze,
    # ale program jej nie zrozumie.
    if ! diff <(tabele "$PGDATABASE") <(tabele "$baza_probna") > /tmp/roznice.txt; then
        zapisz "BŁĄD: odtworzona kopia ma inny zestaw tabel niż baza"
        sed 's/^/        /' /tmp/roznice.txt | tee -a "$DZIENNIK"
        wynik=1
    fi

    for tabela in "${WYMAGANE_TABELE[@]}"; do
        local ile
        ile=$(liczba_wierszy "$baza_probna" "$tabela")

        if [ "$ile" -eq 0 ]; then
            zapisz "BŁĄD: w odtworzonej kopii tabela $tabela jest pusta"
            wynik=1
        fi
    done

    if [ "$wynik" -eq 0 ]; then
        local podsumowanie=""
        for tabela in $(tabele "$baza_probna"); do
            podsumowanie+="$tabela=$(liczba_wierszy "$baza_probna" "$tabela") "
        done
        zapisz "zawartość odtworzonej kopii: $podsumowanie"
    fi

    dropdb --if-exists "$baza_probna"
    return "$wynik"
}

# Kasuje najstarsze sprawdzone kopie ponad ustalone pokolenia. Kopie
# nieudane zostają na dysku - są dowodem, że coś wymaga uwagi.
posprzataj() {
    local nadmiar
    nadmiar=$(ls -1t "$KATALOG"/firmapro-*.dump 2>/dev/null | tail -n "+$((ILE_POKOLEN + 1))")

    for plik in $nadmiar; do
        rm -f "$plik"
        zapisz "usunięto starą kopię $(basename "$plik")"
    done
}

# Jedna runda: zrzut, sprawdzenie przez odtworzenie, sprzątanie.
runda() {
    local znacznik plik
    znacznik=$(date '+%Y%m%d-%H%M%S')
    plik="$KATALOG/firmapro-$znacznik.dump"

    # Zrzut powstaje pod nazwą tymczasową. Plik z nazwą docelową jest więc
    # zawsze kompletny - przerwanie w połowie nie zostawia czegoś, co wygląda
    # na dobrą kopię.
    if ! pg_dump --format=custom --compress=9 --file="$plik.wtoku"; then
        rm -f "$plik.wtoku"
        zapisz "BŁĄD: nie udało się wykonać zrzutu bazy"
        return 1
    fi

    mv "$plik.wtoku" "$plik"
    zapisz "zrobiono kopię $(basename "$plik") ($(du -h "$plik" | cut -f1))"

    if sprawdz_kopie "$plik"; then
        zapisz "kopia $(basename "$plik") odtworzona i sprawdzona"
        posprzataj
        return 0
    fi

    # Plik zostaje, ale pod inną nazwą: nie liczy się jako pokolenie
    # i nie zostanie skasowany przy sprzątaniu.
    mv "$plik" "$plik.NIESPRAWDZONA"
    zapisz "UWAGA: kopii nie udało się sprawdzić - sprzątanie wstrzymane"
    return 1
}

mkdir -p "$KATALOG"

case "${1:-}" in
    sprawdz)
        PLIK="${2:?podaj plik kopii do sprawdzenia}"
        [ -f "$PLIK" ] || PLIK="$KATALOG/$PLIK"
        sprawdz_kopie "$PLIK"
        exit $?
        ;;
    raz)
        runda
        exit $?
        ;;
esac

zapisz "usługa kopii zapasowych wystartowała (co $CO_ILE_GODZIN h, $ILE_POKOLEN pokoleń)"

while true; do
    runda || true
    sleep "$((CO_ILE_GODZIN * 3600))"
done
