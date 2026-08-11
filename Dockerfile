# Obraz programu Firma PRO.
#
# Budowanie i uruchamianie rozdzielone są na dwa etapy: gotowy obraz zawiera
# samo środowisko uruchomieniowe, bez pakietu SDK, kodu źródłowego i pamięci
# podręcznej pakietów. Mniejszy obraz to nie tylko oszczędność miejsca -
# to również mniej rzeczy, w których może znaleźć się luka.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS budowanie
WORKDIR /zrodla

# Najpierw same pliki projektów. Dopóki nie zmieni się lista zależności,
# odtwarzanie pakietów bierze się z pamięci podręcznej budowania.
COPY Directory.Build.props Directory.Packages.props FirmaPro.slnx ./
COPY src/FirmaPro.Domena/FirmaPro.Domena.csproj src/FirmaPro.Domena/
COPY src/FirmaPro.Ksef/FirmaPro.Ksef.csproj     src/FirmaPro.Ksef/
COPY src/FirmaPro.Dane/FirmaPro.Dane.csproj     src/FirmaPro.Dane/
COPY src/FirmaPro.Jpk/FirmaPro.Jpk.csproj       src/FirmaPro.Jpk/
COPY src/FirmaPro.Wydruk/FirmaPro.Wydruk.csproj src/FirmaPro.Wydruk/
COPY src/FirmaPro.Web/FirmaPro.Web.csproj       src/FirmaPro.Web/
RUN dotnet restore src/FirmaPro.Web/FirmaPro.Web.csproj

COPY src/ src/
RUN dotnet publish src/FirmaPro.Web/FirmaPro.Web.csproj \
    --configuration Release --no-restore --output /program

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS uruchomienie
WORKDIR /program

# Katalog kluczy zakładamy jeszcze jako administrator i oddajemy go
# użytkownikowi programu. Wolumin podpięty pod istniejący katalog przejmuje
# jego właściciela - podpięty pod nieistniejący należałby do administratora
# i program nie miałby gdzie zapisać kluczy.
RUN mkdir -p /dane/klucze && chown -R $APP_UID:$APP_UID /dane

# Program nie potrzebuje uprawnień administratora, więc ich nie dostaje.
# Obrazy .NET mają gotowego użytkownika o identyfikatorze 1654.
USER $APP_UID

# Katalog kluczy ochrony danych i katalog wydruków leżą na woluminach -
# zawartość obrazu jest wymienna, ich zawartość nie.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Aplikacja__KatalogKluczy=/dane/klucze

EXPOSE 8080

COPY --from=budowanie /program ./

ENTRYPOINT ["dotnet", "FirmaPro.Web.dll"]
