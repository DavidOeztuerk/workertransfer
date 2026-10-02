# One target per question somebody really asks.
#
# `check` is the gate: builds, checks, tests — and BUILDS AND TESTS IN SEPARATE
# INVOCATIONS. Chained, the Testcontainers suites fail and look exactly like
# real test failures.

.DEFAULT_GOAL := help
DOTNET_SLN := WorkerTransfer.slnx

.PHONY: help check check-dotnet check-web build test test-web validate validate-e2e \
        fix dev env up down images route-map analyze evidence evidence-check \
        control-plane control-plane-down k8s-up k8s-down k8s-lint k8s-seed clean

help:  # This list.
	@# `0-9` in the pattern, otherwise k8s-up/-down/-seed/-lint are missing —
	@# present, but invisible, and so findable by nobody.
	@awk 'BEGIN {FS = ":.*#"} /^[a-zA-Z0-9_-]+:.*# / {printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

check: check-dotnet check-web  # The gate: everything, fail-fast.

check-dotnet: build test  # Build first, then test — in this order.

build:  # Compile. Warnings are errors (Directory.Build.props).
	dotnet build $(DOTNET_SLN)

test:  # The test suites, ONE AT A TIME.
	@# Not `dotnet test` over the solution: fourteen suites then start fourteen
	@# containers at once, Testcontainers' ResourceReaper runs into a timeout,
	@# and ALL suites fall within a millisecond with
	@# TypeInitializationException. That looks like a broken build and is none.
	@./scripts/test-dotnet.sh

check-web:  # Frontend: TypeScript + Vitest + bundling.
	@# `pnpm build` belongs here. tsc and Vitest both do NOT run through the
	@# build path; an error that only shows up when bundling otherwise shows up
	@# only in the image — and nobody builds that here on the side.
	cd web && pnpm check
	cd web && pnpm test
	cd web && pnpm build

test-web:  # Only the frontend suite.
	cd web && pnpm test

validate:  # Like check, but runs through and reports every red step.
	./scripts/validate.sh

analyze:  # Noelia's tool over the composition of this tree (ADR-0045).
	@# Static: it reads literal calls and configuration. What is decided at
	@# runtime it does not see — for that there is
	@# `noelia analyze --url <dashboard>`.
	@#
	@# Exit 0 clean, 1 findings, 2 the tool itself broken. The 2 is the reason
	@# there is no `|| true` here: a tool that does not run is no clean tree.
	@PATH="$$PATH:$$HOME/.dotnet/tools" noelia analyze .

evidence:  # The three signed documents against the running stack (ADR-0044).
	@# Datenschutz, KI, Mitbestimmung — three documents for three readers,
	@# because from a mixed one the one part can be quoted without quoting the
	@# part that limits it. Needs `make up`.
	./scripts/evidence.sh

evidence-check:  # Every check in every service; red as soon as a promise is open.
	@# Red ONLY on `Fail`. A warning is no defect but a question to a human —
	@# a gate that goes red on deliberate restraint gets switched off by the
	@# next person. Needs `make up`.
	./scripts/evidence-check.sh

route-map:  # Drive docs/routenkarte.yml against the running stack.
	@# The COMPLETENESS of the map is checked by RouteMapTests and runs in
	@# every suite. Here the ANSWERS are checked, and that needs the stack:
	@# `make up` first.
	./scripts/route-map.sh

validate-e2e:  # The browser journeys as well; needs the running stack.
	./scripts/validate.sh --e2e

fix:  # Format.
	dotnet format $(DOTNET_SLN)

env:  # Create .env: roll four secrets, generate the token key pair.
	@# The first command in a fresh clone. After it, `make up` runs.
	@#
	@# The secrets stand EMPTY in .env.example — a built-in default would be
	@# the secret itself, and it would sit in git. Here they come about fresh,
	@# different per clone.
	@if [ -f .env ]; then \
		echo ".env exists already — nothing changed."; \
		echo "To roll again: rm .env && make env"; \
	else \
		cp .env.example .env; \
		for s in WORKERTRANSFER_NOTIFY_SECRET WORKERTRANSFER_ERASURE_SECRET WORKERTRANSFER_SECRETS_KEY WORKERTRANSFER_EVIDENCE_SECRET; do \
			value=$$(openssl rand -base64 32); \
			tmp=$$(mktemp); \
			awk -v k="$$s" -v v="$$value" '$$0 == k "=" { print k "=" v; next } { print }' .env > "$$tmp" && mv "$$tmp" .env; \
		done; \
		pem=$$(mktemp); \
		openssl ecparam -name prime256v1 -genkey -noout -out "$$pem" 2>/dev/null; \
		private=$$(openssl pkcs8 -topk8 -nocrypt -in "$$pem" -outform DER 2>/dev/null | base64 | tr -d '\n'); \
		public=$$(openssl ec -in "$$pem" -pubout -outform DER 2>/dev/null | base64 | tr -d '\n'); \
		rm -f "$$pem"; \
		for pair in "WORKERTRANSFER_JWT_PRIVATE_KEY=$$private" "WORKERTRANSFER_JWT_PUBLIC_KEY=$$public"; do \
			k=$${pair%%=*}; v=$${pair#*=}; \
			tmp=$$(mktemp); \
			awk -v k="$$k" -v v="$$v" '$$0 == k "=" { print k "=" v; next } { print }' .env > "$$tmp" && mv "$$tmp" .env; \
		done; \
		echo ".env created: four secrets rolled, one key pair generated."; \
		echo "It is ignored and does not belong in git."; \
	fi

up:  # The whole stack locally: Postgres, Mailpit, fourteen services, gateway, UI.
	docker compose up -d --build

down:  # Stop. `make down ARGS=-v` throws the databases away too.
	docker compose down $(ARGS)

dev: up  # Alias for `up` — the stack IS the development environment.

k8s-up:  # Local staging environment on kind — build, roll out, PROVE.
	./scripts/k8s-up.sh

k8s-down:  # Delete the kind cluster together with its data.
	./scripts/k8s-down.sh

k8s-seed:  # Test data into the running environment: company, three jobs, an applicant account.
	./scripts/k8s-seed.sh

control-plane:  # The Noelia Control Plane over the running stack: http://localhost:8091
	@# On the host, not in a container: its local mode accepts only loopback
	@# peers, and Docker's port forwarding never is one. The reason stands in
	@# the script. The path can be redirected with NOELIA_CONTROL_PLANE_PFAD.
	./scripts/control-plane.sh

control-plane-down:  # Stop the control plane.
	./scripts/control-plane.sh --down

images:  # Build both shipped images. The local twin of the CI job.
	@# A green check that builds no image says nothing about what is shipped:
	@# the jump to node 25 got through that way and broke the UI image (node 25
	@# no longer ships corepack).
	docker build -f docker/dotnet-service.Dockerfile \
		--secret "id=nuget_config,src=$(HOME)/.nuget/NuGet/NuGet.Config" \
		-t workertransfer-dotnet:local .
	docker build -f docker/web-prod.Dockerfile -t workertransfer/web:local .

# The two key values are THROWAWAY values and are called that. The chart
# demands them (`required`), because a rolled pair does not fit together — but
# here it is only rendered, never applied. Without them this target does not
# check the chart to the end.
HELM_PROBE = --set-file postgres.initSql=scripts/initdb/01-create-service-databases.sql \
	--set secrets.jwtPrivateKey=throwaway --set secrets.jwtPublicKey=throwaway

k8s-lint:  # Check the chart without a cluster: helm lint + render.
	helm lint deploy/helm/workertransfer $(HELM_PROBE)
	helm template deploy/helm/workertransfer $(HELM_PROBE) > /dev/null
	@echo "Chart renders."

clean:  # Build and test leftovers.
	find src tests -type d \( -name bin -o -name obj -o -name TestResults \) -prune -exec rm -rf {} +
