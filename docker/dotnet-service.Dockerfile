# syntax=docker/dockerfile:1
#
# ONE image for all fifteen services — the fourteen domain ones and the gateway.
#
# They differ in exactly one environment variable, `SERVICE_DIR`, which the
# container sets. Fifteen images that differ in one variable would not be
# fifteen times the software (ADR-0028) — and they would be fifteen
# opportunities to forget one of them.
#
# That is why `SERVICE_DIR` does NOT stand here as a build argument: the image
# is built once, without knowing who it will be later.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

# The manifests first, then the rest: that way the restore stays in the layer
# cache as long as no dependency changes.
COPY Directory.Build.props Directory.Packages.props NuGet.Config \
     WorkerTransfer.slnx ./
COPY src ./src
COPY tests ./tests

# Noelia lives on nuget.org and demands no sign-in. It used to come in as a
# BuildKit secret and NEVER becomes a layer: a credential in an image stays in
# every backup and in every `docker history`.
#
# --locked does not exist for NuGet as it did for uv; `restore` reads the
# central versions from Directory.Packages.props, and those stand in the tree.
# WITHOUT A SECRET since Girder lay on nuget.org (10.09.2026) and all the more
# since Noelia (ADR-0045). Here stood a
# `--mount=type=secret,id=nuget_config` — as a secret and not as a COPY, so the
# sign-in never became an image layer. The reason is gone: there is nothing
# left to sign in to.
RUN \
    dotnet restore WorkerTransfer.slnx

# Every entry point into its own directory. The key is the service name as
# `SERVICE_DIR` carries it.
RUN set -eu; \
    for entry in \
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
        "scout:src/scout-service/WorkerTransfer.Scout.Api" \
        "advisor:src/advisor-service/WorkerTransfer.Advisor.Api" \
        "assessment:src/assessment-service/WorkerTransfer.Assessment.Api" \
        "messages:src/messages-service/WorkerTransfer.Messages.Api" \
        "notification:src/notification-service/WorkerTransfer.Notification.Api" \
        "gateway:src/gateway/WorkerTransfer.Gateway" \
    ; do \
        name="${entry%%:*}"; path="${entry#*:}"; \
        echo "==> ${name}"; \
        dotnet publish "${path}" -c Release -o "/published/${name}" --no-restore; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl belongs in the runtime image, and for ONE reason: the container has to
# be able to answer its own health probe.
#
# Docker asks from INSIDE — `healthcheck:` in docker-compose.yml runs in the
# container. The runtime image, however, ships neither curl nor wget nor nc,
# and `sh` is dash, so it cannot do /dev/tcp either. Without this package the
# probe has nothing to ask with; that is why `dotnet --version` stood there —
# and that ALWAYS FAILS on a runtime image (exit code 155, "No .NET SDKs were
# found"). Every service then reported `unhealthy` while answering perfectly,
# and nobody believed the reports any more.
#
# Kubernetes does NOT need this — there the kubelet asks over HTTP itself and
# stands outside. So only compose pays the two megabytes.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Not as root. The image serves HTTP and writes nothing but logs.
# USER stays root here so the entrypoint can chown a named volume; it switches
# to uid 10001 itself before `dotnet` starts. A `USER 10001` at this point let
# POST /resumes/me/documents die with 500: the document store's volume comes in
# as root:root.
RUN useradd --uid 10001 --create-home --shell /usr/sbin/nologin service

WORKDIR /app
COPY --from=build --chown=10001:10001 /published /app

COPY docker/dotnet-entrypoint.sh /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
