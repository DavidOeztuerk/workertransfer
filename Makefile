# Ein Ziel je Frage, die jemand wirklich stellt.
#
# `check` ist das Tor: baut, prueft, testet — und BAUT UND TESTET IN GETRENNTEN
# AUFRUFEN. Verkettet scheitern die Testcontainers-Reihen und sehen dabei aus
# wie echte Testfehler.

.DEFAULT_GOAL := help
DOTNET_SLN := WorkerTransfer.slnx

.PHONY: help check check-dotnet check-web build test test-web validate validate-e2e \
        fix dev env up down images routenkarte k8s-up k8s-down k8s-lint k8s-seed clean

help:  # Diese Liste.
	@# `0-9` im Muster, sonst fehlen k8s-up/-down/-seed/-lint — vorhanden, aber
	@# unsichtbar, und damit fuer niemanden auffindbar.
	@awk 'BEGIN {FS = ":.*#"} /^[a-zA-Z0-9_-]+:.*# / {printf "  \033[36m%-13s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

check: check-dotnet check-web  # Das Tor: alles, fail-fast.

check-dotnet: build test  # Erst bauen, dann testen — in dieser Reihenfolge.

build:  # Uebersetzen. Warnungen sind Fehler (Directory.Build.props).
	dotnet build $(DOTNET_SLN)

test:  # Die Testreihen, EINZELN.
	@# Nicht `dotnet test` ueber die Projektmappe: vierzehn Reihen starten dann
	@# vierzehn Container gleichzeitig, der ResourceReaper von Testcontainers
	@# laeuft in eine Zeitueberschreitung, und ALLE Reihen fallen binnen einer
	@# Millisekunde mit TypeInitializationException. Das sieht aus wie ein
	@# kaputter Bau und ist keiner.
	@./scripts/test-dotnet.sh

check-web:  # Frontend: TypeScript + Vitest + Buendeln.
	@# `pnpm build` gehoert dazu. tsc und Vitest laufen beide NICHT ueber den
	@# Bauweg; ein Fehler, der erst beim Buendeln auftritt, faellt sonst erst im
	@# Bild auf — und das baut hier niemand nebenbei.
	cd web && pnpm check
	cd web && pnpm test
	cd web && pnpm build

test-web:  # Nur die Frontend-Reihe.
	cd web && pnpm test

validate:  # Wie check, aber laeuft durch und berichtet jeden roten Schritt.
	./scripts/validate.sh

routenkarte:  # docs/routenkarte.yml gegen den laufenden Stapel fahren.
	@# Die VOLLSTAENDIGKEIT der Karte prueft RoutenkarteTests und laeuft in
	@# jeder Reihe mit. Hier werden die ANTWORTEN geprueft, und dafuer braucht
	@# es den Stapel: `make up` zuerst.
	./scripts/routenkarte.sh

validate-e2e:  # Zusaetzlich die Browser-Reise; braucht den laufenden Stapel.
	./scripts/validate.sh --e2e

fix:  # Formatieren.
	dotnet format $(DOTNET_SLN)

env:  # .env aus der Vorlage anlegen und die drei Geheimnisse wuerfeln.
	@# Der erste Befehl in einem frischen Klon. Danach laeuft `make up`.
	@#
	@# Die Geheimnisse stehen in .env.example LEER — ein eingebauter Vorgabewert
	@# waere das Geheimnis selbst, und es laege in git. Hier entstehen sie neu,
	@# je Klon andere.
	@if [ -f .env ]; then \
		echo ".env gibt es schon — nichts geaendert."; \
		echo "Zum Neuwuerfeln: rm .env && make env"; \
	else \
		cp .env.example .env; \
		for s in WORKERTRANSFER_JWT_SECRET WORKERTRANSFER_NOTIFY_SECRET WORKERTRANSFER_ERASURE_SECRET; do \
			wert=$$(openssl rand -base64 32); \
			tmp=$$(mktemp); \
			awk -v k="$$s" -v v="$$wert" '$$0 == k "=" { print k "=" v; next } { print }' .env > "$$tmp" && mv "$$tmp" .env; \
		done; \
		echo ".env angelegt, drei Geheimnisse frisch gewuerfelt."; \
		echo "Sie ist ignoriert und gehoert nicht in git."; \
	fi

up:  # Der ganze Stapel lokal: Postgres, Mailpit, elf Dienste, Gateway, Oberflaeche.
	docker compose up -d --build

down:  # Anhalten. `make down ARGS=-v` wirft auch die Datenbanken weg.
	docker compose down $(ARGS)

dev: up  # Alias fuer `up` — der Stapel IST die Entwicklungsumgebung.

k8s-up:  # Lokale Staging-Umgebung auf kind — bauen, ausrollen, BELEGEN.
	./scripts/k8s-up.sh

k8s-down:  # Den kind-Cluster samt Daten loeschen.
	./scripts/k8s-down.sh

k8s-seed:  # Testdaten in die laufende Umgebung: Firma, drei Stellen, ein Bewerber-Konto.
	./scripts/k8s-seed.sh

images:  # Beide ausgelieferten Bilder bauen. Der lokale Zwilling des CI-Jobs.
	@# Eine gruene Pruefung, die kein Bild baut, sagt nichts ueber das, was
	@# ausgeliefert wird: der Sprung auf node 25 kam so durch und zerlegte das
	@# Oberflaechenbild (node 25 bringt kein corepack mehr mit).
	docker build -f docker/dotnet-service.Dockerfile \
		--secret "id=nuget_config,src=$(HOME)/.nuget/NuGet/NuGet.Config" \
		-t workertransfer-dotnet:local .
	docker build -f docker/web-prod.Dockerfile -t workertransfer/web:local .

k8s-lint:  # Chart pruefen, ohne Cluster: helm lint + rendern.
	helm lint deploy/helm/workertransfer \
		--set-file postgres.initSql=scripts/initdb/01-create-service-databases.sql
	helm template deploy/helm/workertransfer \
		--set-file postgres.initSql=scripts/initdb/01-create-service-databases.sql > /dev/null
	@echo "Chart rendert."

clean:  # Bau- und Testreste.
	find src tests -type d \( -name bin -o -name obj -o -name TestResults \) -prune -exec rm -rf {} +
