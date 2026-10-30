# ADR-0056 — Dienst-, Datenbank- und Mail-Ports nur auf Loopback

**Datum:** 27.10.2026 · **Status:** angenommen
**Berührt:** ADR-0028 (ein Image, Compose), ADR-0040 (das Gateway ist der Eingang),
Quest Q-0003 / Gate G-0001 (Monarch-Backlog, Befunde D5, N-1, WT-5)

## Zusammenhang

`docker-compose.yml` veröffentlichte 20 Ports, alle als `"PORT:PORT"`. Docker
bindet diese Form an **jede** Schnittstelle des Hosts. Damit erreichte jeder im
selben Netz:

- die fünfzehn Dienste direkt, also `/internal/*` (nur durch das geteilte
  Geheimnis geschützt), `/noelia` und `identity-service`s `/auth/login` —
  **am Gateway vorbei und damit an seiner Bremse vorbei**;
- Postgres auf 5432 mit dem Entwicklungsbenutzer;
- Mailpit auf 1025/8025, also jede Bestätigungs- und Einladungsmail.

Gemessen am 27.10.2026 mit `docker compose config` (ohne Daemon, ohne `.env`):
18 der 20 Ports ohne `host_ip`.

## Entscheidung

**Nur das Gateway (8090) und die Oberfläche (5173) bleiben von außen
erreichbar.** Alle anderen veröffentlichten Ports binden an `127.0.0.1`:
`"127.0.0.1:8001:8001"` usw., ebenso 5432, 1025 und 8025.

Der Host selbst erreicht weiterhin alles. Das ist die Bedingung, an der die
Entscheidung hängt: die Control Plane läuft auf dem Host und sammelt über genau
diese Ports (`scripts/control-plane-fleet.json`), die Skripte unter `scripts/`
und Werkzeuge wie `psql` ebenso.

`ComposePortTests` (CrossCutting) liest `docker-compose.yml` und wird rot, sobald
ein Port außer Gateway und Oberfläche ohne `127.0.0.1` veröffentlicht wird. Vor
der Änderung war er rot (Gegenprobe), danach grün.

## Folgen

- **`localhost` löst auf macOS zuerst auf `::1` auf.** Docker bindet jetzt nur
  IPv4. Ein Aufrufer, der nur `::1` versucht, bekommt keine Verbindung. curl und
  .NET versuchen danach `127.0.0.1` — zu bestätigen mit `make control-plane`
  (15/15) am laufenden Stack, bevor der PR gemergt wird.
- Wer aus einer anderen Maschine (Handy im WLAN) testen will, geht über das
  Gateway oder setzt die Bindung lokal per `docker-compose.override.yml` zurück —
  bewusst und sichtbar, nicht als Vorgabe.
- Kubernetes ist nicht berührt: die Chart veröffentlicht ihre Ports selbst
  (kind, NodePorts) und ist eine eigene Frage.
