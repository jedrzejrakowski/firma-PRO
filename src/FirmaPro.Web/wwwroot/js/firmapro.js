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
