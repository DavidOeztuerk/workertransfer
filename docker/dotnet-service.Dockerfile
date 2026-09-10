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
# OHNE GEHEIMNIS, seit Girder auf nuget.org liegt (10.09.2026). Hier stand ein
# `--mount=type=secret,id=nuget_config` — als Geheimnis und nicht als COPY,
# damit die Anmeldung nie eine Bildschicht wird. Der Grund ist weg: es gibt
# nichts mehr anzumelden.
RUN \
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

# curl gehoert ins Laufzeitbild, und zwar aus EINEM Grund: der Behaelter muss
# seine eigene Gesundheitsprobe beantworten koennen.
#
# Docker fragt von INNEN — `healthcheck:` in docker-compose.yml laeuft im
# Behaelter. Das Laufzeitbild bringt aber weder curl noch wget noch nc mit, und
# `sh` ist dash, kann also auch kein /dev/tcp. Ohne dieses Paket hat die Probe
# nichts, womit sie fragen koennte; dort stand deshalb `dotnet --version` — und
# das SCHEITERT auf einem Laufzeitbild immer (Abbruchcode 155, "No .NET SDKs
# were found"). Jeder Dienst meldete daraufhin `unhealthy`, waehrend er
# tadellos antwortete, und niemand hat den Meldungen mehr geglaubt.
#
# Kubernetes braucht das NICHT — dort fragt das Kubelet selbst ueber HTTP und
# steht ausserhalb. Die zwei Megabyte zahlt also nur Compose.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Nicht als root. Das Bild bedient HTTP und schreibt nichts ausser Protokoll.
# USER bleibt hier root, damit der Entrypoint ein named Volume chownen
# kann; er wechselt selbst auf uid 10001, bevor `dotnet` startet. Ein
# `USER 10001` an dieser Stelle liess POST /resumes/me/documents mit
# 500 sterben: /daten/ablage kommt als root:root ins Volume.
RUN useradd --uid 10001 --create-home --shell /usr/sbin/nologin dienst

WORKDIR /app
COPY --from=bau --chown=10001:10001 /veroeffentlicht /app

COPY docker/dotnet-entrypoint.sh /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
