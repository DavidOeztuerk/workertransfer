# syntax=docker/dockerfile:1
#
# EIN Bild fuer alle elf Dienste — die zehn fachlichen und das Gateway.
#
# Sie unterscheiden sich in genau einer Umgebungsvariablen, `SERVICE_DIR`, die
# der Behaelter setzt. Elf Bilder, die sich in einer Variablen unterscheiden,
# waeren nicht elfmal die Software (ADR-0028) — und sie waeren elf Gelegenheiten,
# eines davon zu vergessen.
#
# Deshalb steht `SERVICE_DIR` hier NICHT als Bauargument: das Bild wird einmal
# gebaut, ohne zu wissen, wer es spaeter ist.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS bau

WORKDIR /quelle

# Erst die Manifeste, dann der Rest: so bleibt die Wiederherstellung in der
# Schichtzwischenablage, solange sich keine Abhaengigkeit aendert.
COPY Directory.Build.props Directory.Packages.props NuGet.Config \
     WorkerTransfer.slnx ./
COPY src ./src
COPY tests ./tests

# Girder liegt in GitHub Packages und verlangt eine Anmeldung. Sie kommt als
# BuildKit-Geheimnis herein und wird NIE eine Schicht: ein Zugriff in einem Bild
# bleibt in jeder Sicherung und in jedem `docker history`.
#
# --locked gibt es fuer NuGet nicht wie fuer uv; `restore` liest die zentralen
# Fassungen aus Directory.Packages.props, und die stehen im Baum.
RUN --mount=type=secret,id=nuget_config,target=/root/.nuget/NuGet/NuGet.Config \
    dotnet restore WorkerTransfer.slnx

# Jeder Einstiegspunkt in sein eigenes Verzeichnis. Der Schluessel ist der
# Dienstname, wie ihn `SERVICE_DIR` traegt.
RUN set -eu; \
    for eintrag in \
        "identity:src/identity-service/WorkerTransfer.Identity.Api" \
        "consent:src/consent-service/WorkerTransfer.Consent.Api" \
        "profile:src/profile-service/WorkerTransfer.Profile.Api" \
        "resume:src/resume-service/WorkerTransfer.Resume.Api" \
        "portfolio:src/portfolio-service/WorkerTransfer.Portfolio.Api" \
        "jobs:src/jobs-service/WorkerTransfer.Jobs.Api" \
        "applications:src/applications-service/WorkerTransfer.Applications.Api" \
        "companies:src/companies-service/WorkerTransfer.Companies.Api" \
        "transfer:src/transfer-service/WorkerTransfer.Transfer.Api" \
        "github:src/github-service/WorkerTransfer.GitHub.Api" \
        "notification:src/notification-service/WorkerTransfer.Notification.Api" \
        "gateway:src/gateway/WorkerTransfer.Gateway" \
    ; do \
        name="${eintrag%%:*}"; pfad="${eintrag#*:}"; \
        echo "==> ${name}"; \
        dotnet publish "${pfad}" -c Release -o "/veroeffentlicht/${name}" --no-restore; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS laufzeit

# Nicht als root. Das Bild bedient HTTP und schreibt nichts ausser Protokoll.
RUN useradd --uid 10001 --create-home --shell /usr/sbin/nologin dienst
USER 10001

WORKDIR /app
COPY --from=bau --chown=10001:10001 /veroeffentlicht /app

COPY docker/dotnet-entrypoint.sh /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
