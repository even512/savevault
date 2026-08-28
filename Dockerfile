# SaveVault Server — Docker-Image (linux-x64). Baut nur Server + Core;
# der WPF-Client (net9.0-windows) ist nicht Teil des Images.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Nur die für den Server nötigen Projektdateien restoren (Cache-freundlich)
COPY src/SaveVault.Core/SaveVault.Core.csproj src/SaveVault.Core/
COPY src/SaveVault.Server/SaveVault.Server.csproj src/SaveVault.Server/
RUN dotnet restore src/SaveVault.Server/SaveVault.Server.csproj

# Quellen kopieren und veröffentlichen
COPY src/SaveVault.Core/ src/SaveVault.Core/
COPY src/SaveVault.Server/ src/SaveVault.Server/
RUN dotnet publish src/SaveVault.Server/SaveVault.Server.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Standard-Konfiguration (per Umgebungsvariablen/.env überschreibbar).
# Der Lauscht-Port wird aus SAVEVAULT_PORT abgeleitet (der Server bindet selbst
# http://0.0.0.0:$SAVEVAULT_PORT, solange ASPNETCORE_URLS nicht gesetzt ist) – so ist
# SAVEVAULT_PORT die EINZIGE Port-Quelle. Ein Server-Token gibt es nicht mehr: das
# Dashboard-Konto (Benutzer + Passwort) wird beim ersten Aufruf im Dashboard angelegt.
ENV SAVEVAULT_PORT=8420
ENV SAVEVAULT_DATA=/data/savevault
EXPOSE 8420
VOLUME ["/data/savevault"]

ENTRYPOINT ["dotnet", "SaveVault.Server.dll"]
