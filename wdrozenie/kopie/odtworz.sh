#!/bin/bash
# Odtworzenie bazy z kopii zapasowej.
#
# To jedyny skrypt w tym repozytorium, który kasuje dane. Zatrzymaj najpierw
# program, żeby nikt nie pisał do bazy w trakcie:
#
#   docker compose stop program
#   docker compose run --rm kopie bash /skrypty/odtworz.sh firmapro-20260811-0300.dump
#   docker compose start program
#
# Bez podania nazwy pliku skrypt wypisuje dostępne kopie i nic nie robi.

set -euo pipefail

KATALOG="${KATALOG:-/kopie}"
PLIK="${1:-}"

if [ -z "$PLIK" ]; then
    echo "Dostępne kopie w $KATALOG:"
    echo
    ls -1sht "$KATALOG"/firmapro-*.dump 2>/dev/null || echo "  (brak kopii)"
    echo
    echo "Użycie: odtworz.sh <nazwa-pliku>"
    exit 1
fi

# Pozwalamy podać samą nazwę albo pełną ścieżkę.
[ -f "$PLIK" ] || PLIK="$KATALOG/$PLIK"

if [ ! -f "$PLIK" ]; then
    echo "Nie ma pliku $PLIK" >&2
    exit 1
fi

echo "Kopia:    $(basename "$PLIK") z $(date -r "$PLIK" '+%Y-%m-%d %H:%M')"
echo "Baza:     $PGDATABASE na $PGHOST"
echo
echo "Cała obecna zawartość bazy $PGDATABASE zostanie skasowana i zastąpiona"
echo "zawartością kopii. Tej operacji nie da się cofnąć."
echo
read -r -p "Wpisz nazwę bazy, żeby potwierdzić: " potwierdzenie

if [ "$potwierdzenie" != "$PGDATABASE" ]; then
    echo "Przerwane - nic nie zostało zmienione."
    exit 1
fi

# Zanim skasujemy cokolwiek, robimy zrzut stanu obecnego. Gdyby odtwarzana
# kopia okazała się nie tą, o którą chodziło, jest dokąd wrócić.
RATUNEK="$KATALOG/przed-odtworzeniem-$(date '+%Y%m%d-%H%M%S').dump"
echo "Zapisuję obecny stan bazy do $(basename "$RATUNEK")..."
pg_dump --format=custom --compress=9 --file="$RATUNEK"

echo "Odtwarzam..."
psql -d postgres -c "DROP DATABASE \"$PGDATABASE\" WITH (FORCE)"
psql -d postgres -c "CREATE DATABASE \"$PGDATABASE\" OWNER \"$PGUSER\""
pg_restore --dbname="$PGDATABASE" --exit-on-error --no-owner "$PLIK"

echo
echo "Gotowe. Uruchom program: docker compose start program"
