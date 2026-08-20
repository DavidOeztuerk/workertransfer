# WorkerTransfer auf .NET

Die Migration von Python nach .NET, auf [Girder](https://github.com/DavidOeztuerk/girder).
Der Plan steht in [`../docs/MIGRATION-PROMPT.md`](../docs/MIGRATION-PROMPT.md)
— lies ihn, bevor du hier etwas anfasst.

## Warum ein eigener Ordner

Python liegt in `../apps` und `../packages`, das Frontend in `../apps/web`. Ein
eigener Zweig hält die drei Ökosysteme auseinander, solange sie nebeneinander
laufen. Wenn Python geht, kann das hier flach gezogen werden.

## Was schon steht

```
WorkerTransfer.slnx
Directory.Build.props      net10.0, Warnungen sind Fehler
Directory.Packages.props   zentrale Paketverwaltung, Girder 2.3.0
NuGet.Config               Quellzuordnung: Girder.* nur von GitHub Packages
src/identity-service/      fünf Projekte, eins je Schicht
tests/                     eine Reihe je Dienst
```

Nur das Gerüst. Kein Dienst tut etwas — der erste Schritt ist die dünne Scheibe
von identity-service, und der Prompt sagt, welche.

## Ein Projekt je Schicht

```
WorkerTransfer.Identity.Domain           Girder.Core
WorkerTransfer.Identity.Contracts        Girder.Contracts
WorkerTransfer.Identity.Application      Girder.Abstractions, Girder.Application
WorkerTransfer.Identity.Infrastructure   Girder.Infrastructure, .Data.EntityFrameworkCore,
                                         .Passwords.BCrypt, Npgsql
WorkerTransfer.Identity.Api              Girder.Infrastructure, Girder.InMemory
```

Getrennte Projekte, damit der **Übersetzer** die Richtung erzwingt statt der
Disziplin. Nachgeprüft: eine `using Girder.Infrastructure…`-Zeile in der
Domänenschicht ergibt `CS0234`, weil es dort keinen Verweis darauf gibt.

`Girder.Passwords.BCrypt` liegt hier, weil der Python-Dienst bcrypt schreibt.
Es liest diese Einträge, während neue im aktuellen Verfahren geschrieben
werden; jede Anmeldung holt eine Person herüber.

## Bauen und prüfen

```bash
dotnet build WorkerTransfer.slnx
dotnet test WorkerTransfer.slnx
```

Der Zugriff auf GitHub Packages braucht ein Token mit `read:packages` in der
NuGet-Konfiguration des Rechners.

## Einen weiteren Dienst anlegen

Alles per CLI, nie von Hand:

```bash
S=src/consent-service
for layer in Domain Contracts Application Infrastructure; do
  dotnet new classlib -n "WorkerTransfer.Consent.$layer" -o "$S/WorkerTransfer.Consent.$layer"
done
dotnet new web   -n WorkerTransfer.Consent.Api   -o "$S/WorkerTransfer.Consent.Api"
dotnet new xunit -n WorkerTransfer.Consent.Tests -o tests/WorkerTransfer.Consent.Tests

dotnet sln WorkerTransfer.slnx add $(find "$S" tests/WorkerTransfer.Consent.Tests -name "*.csproj")
```

Danach die Schichtverweise setzen und **die `Version="…"` aus den erzeugten
Projektdateien entfernen** — die Vorlagen schreiben sie hinein, und die
zentrale Paketverwaltung lehnt das mit `NU1008` ab.
