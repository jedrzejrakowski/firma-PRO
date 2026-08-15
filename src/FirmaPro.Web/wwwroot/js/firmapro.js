// Drobna obsługa powłoki programu: menu rozwijane i zakładki.
//
// Świadomie bez żadnej biblioteki - całość mieści się w kilkudziesięciu
// linijkach, a każdy megabajt frameworka to megabajt do pobrania przy
// pierwszym otwarciu programu. Strony działają też bez tego pliku: menu
// rozwijane to skróty do stron, które mają własne adresy.

(function () {
    'use strict';

    var otwarte = null;

    function zamknij() {
        if (!otwarte) {
            return;
        }

        otwarte.lista.hidden = true;
        otwarte.przycisk.setAttribute('aria-expanded', 'false');
        otwarte = null;
    }

    function otworz(przycisk, lista) {
        zamknij();
        lista.hidden = false;
        przycisk.setAttribute('aria-expanded', 'true');
        otwarte = { przycisk: przycisk, lista: lista };
    }

    document.addEventListener('click', function (zdarzenie) {
        var przycisk = zdarzenie.target.closest('[data-rozwija]');

        if (przycisk) {
            zdarzenie.preventDefault();
            var lista = document.getElementById(przycisk.getAttribute('data-rozwija'));

            if (!lista) {
                return;
            }

            if (otwarte && otwarte.lista === lista) {
                zamknij();
            } else {
                otworz(przycisk, lista);
            }

            return;
        }

        // Klik w samo menu (np. w formularz wylogowania) nie ma go zamykać -
        // zamykamy dopiero klik poza nim.
        if (otwarte && !zdarzenie.target.closest('.menu-lista')) {
            zamknij();
        }
    });

    document.addEventListener('keydown', function (zdarzenie) {
        if (zdarzenie.key === 'Escape') {
            zamknij();
        }
    });

    // ------------------------------------------------------------ szukanie
    //
    // Pole samo w sobie jest zwykłym formularzem i działa bez tego kodu -
    // Enter otwiera pełny ekran wyników. Poniższe dokłada tylko podpowiedzi
    // pod polem, żeby najczęstszy przypadek („znajdź tę jedną fakturę”)
    // kończył się jednym kliknięciem zamiast przeładowaniem strony.

    var pole = document.getElementById('szukaj');
    var lista = document.getElementById('podpowiedzi');

    if (pole && lista) {
        var czekanie = null;
        var ostatnie = '';
        var przerwij = null;
        var wybrany = -1;

        function pozycje() {
            return Array.prototype.slice.call(
                lista.querySelectorAll('.podpowiedz, .podpowiedzi-wszystko'));
        }

        function zaznacz(nowy) {
            var lp = pozycje();

            if (lp.length === 0) {
                return;
            }

            // Zawijamy listę: strzałka w górę z pierwszej pozycji wraca na
            // ostatnią, bo tak zachowuje się każde inne menu w systemie.
            wybrany = (nowy + lp.length) % lp.length;

            lp.forEach(function (element, numer) {
                element.classList.toggle('wybrana', numer === wybrany);
            });

            lp[wybrany].scrollIntoView({ block: 'nearest' });
        }

        function schowaj() {
            lista.hidden = true;
            pole.setAttribute('aria-expanded', 'false');
            wybrany = -1;
        }

        function pokaz() {
            if (lista.innerHTML.trim() !== '') {
                lista.hidden = false;
                pole.setAttribute('aria-expanded', 'true');
            }
        }

        function doczytaj() {
            var fraza = pole.value.trim();

            if (fraza === ostatnie) {
                pokaz();
                return;
            }

            ostatnie = fraza;

            if (fraza.length < 2) {
                lista.innerHTML = '';
                schowaj();
                return;
            }

            // Każde nowe wciśnięcie klawisza unieważnia poprzednie zapytanie -
            // inaczej wolniejsza odpowiedź sprzed dwóch liter potrafiłaby
            // nadpisać świeższą.
            if (przerwij) {
                przerwij.abort();
            }

            przerwij = new AbortController();

            fetch('/Szukaj?handler=Podpowiedzi&q=' + encodeURIComponent(fraza),
                  { signal: przerwij.signal, headers: { 'X-Requested-With': 'fetch' } })
                .then(function (odpowiedz) {
                    return odpowiedz.ok ? odpowiedz.text() : '';
                })
                .then(function (html) {
                    lista.innerHTML = html;
                    wybrany = -1;
                    pokaz();
                })
                .catch(function () {
                    // Zerwane połączenie nie jest błędem, o którym warto
                    // krzyczeć - pole dalej działa Enterem.
                });
        }

        pole.addEventListener('input', function () {
            window.clearTimeout(czekanie);
            czekanie = window.setTimeout(doczytaj, 180);
        });

        pole.addEventListener('focus', pokaz);

        pole.addEventListener('keydown', function (zdarzenie) {
            if (zdarzenie.key === 'ArrowDown') {
                zdarzenie.preventDefault();
                pokaz();
                zaznacz(wybrany + 1);
            } else if (zdarzenie.key === 'ArrowUp') {
                zdarzenie.preventDefault();
                zaznacz(wybrany - 1);
            } else if (zdarzenie.key === 'Enter' && wybrany >= 0 && !lista.hidden) {
                zdarzenie.preventDefault();
                pozycje()[wybrany].click();
            } else if (zdarzenie.key === 'Escape') {
                schowaj();
            }
        });

        document.addEventListener('click', function (zdarzenie) {
            if (!zdarzenie.target.closest('.szukajka')) {
                schowaj();
            }
        });

        // Ctrl+K (na Macu Cmd+K) - ten sam skrót, co w większości programów.
        document.addEventListener('keydown', function (zdarzenie) {
            if ((zdarzenie.ctrlKey || zdarzenie.metaKey) && zdarzenie.key === 'k') {
                zdarzenie.preventDefault();
                pole.focus();
                pole.select();
            }
        });
    }

    // Zakładki wewnątrz ekranu: przełączają widoczne panele bez odpytywania
    // serwera. Dane obu zakładek są już na stronie, więc przełączenie jest
    // natychmiastowe i nie gubi pozycji przewijania.
    document.querySelectorAll('[role="tablist"]').forEach(function (pasek) {
        pasek.addEventListener('click', function (zdarzenie) {
            var zakladka = zdarzenie.target.closest('[role="tab"]');

            if (!zakladka) {
                return;
            }

            pasek.querySelectorAll('[role="tab"]').forEach(function (inna) {
                var wybrana = inna === zakladka;
                inna.setAttribute('aria-selected', wybrana ? 'true' : 'false');

                var panel = document.getElementById(inna.getAttribute('aria-controls'));

                if (panel) {
                    panel.hidden = !wybrana;
                }
            });

            // Adres ma pamiętać wybraną zakładkę - inaczej powrót ze
            // szczegółów faktury zawsze wracałby na pierwszą.
            var nazwa = zakladka.getAttribute('data-zakladka');

            if (nazwa && window.history.replaceState) {
                var adres = new URL(window.location.href);
                adres.searchParams.set('widok', nazwa);
                window.history.replaceState(null, '', adres);
            }
        });
    });
})();
