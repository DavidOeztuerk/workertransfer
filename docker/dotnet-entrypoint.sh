#!/usr/bin/env sh
# Which of the services am I?
#
# `SERVICE_DIR` comes from the ENVIRONMENT, not from a build argument: the image
# is built once and does not know at build time who it will be later
# (ADR-0028).
#
# The schema migration does NOT stand here, unlike in the Python days:
# `dotnet ef` needs the SDK, and a runtime image has none. Every service
# migrates its schema itself at startup (`SchemaMigration`) — the same promise,
# only one layer deeper.
set -eu

# Named volumes mount as root:root. A `chown` in the Dockerfile never
# reaches them: the mount overlays the directory. Measured 08.09.2026:
# POST /resumes/me/documents answered 500 UnauthorizedAccessException on
# /daten/ablage/<subject> (the path's name then; today /data/document-store)
# — the handler ran, the write did not.
#
# If we started as root, take ownership of the dirs we actually write and
# drop to uid 10001. If the runtime already runs as 10001 (Kubernetes
# runAsUser / fsGroup), skip this and leave the volume to the platform.
if [ "$(id -u)" = "0" ]; then
  for d in /data/document-store \
           /var/lib/workertransfer/portfolio \
           /var/lib/workertransfer/portfolio-attachments; do
    if [ -d "$d" ]; then
      chown 10001:10001 "$d"
    fi
  done
  exec setpriv --reuid=10001 --regid=10001 --init-groups \
    -- /usr/local/bin/entrypoint.sh "$@"
fi

if [ -z "${SERVICE_DIR:-}" ]; then
  echo "entrypoint: SERVICE_DIR is not set" >&2
  exit 1
fi

DIRECTORY="/app/${SERVICE_DIR}"

if [ ! -d "$DIRECTORY" ]; then
  echo "entrypoint: there is no '${SERVICE_DIR}' in this image" >&2
  echo "present: $(ls /app | tr '\n' ' ')" >&2
  exit 1
fi

# The entry point is the only assembly with a runtimeconfig.json of its own —
# the libraries beside it have none. That saves a table from service name to
# DLL name that would need maintaining with every new service and would, at
# the first omission, silently start the wrong thing.
CONFIG="$(ls "${DIRECTORY}"/*.runtimeconfig.json 2>/dev/null | head -1)"

if [ -z "$CONFIG" ]; then
  echo "entrypoint: no entry point in ${DIRECTORY}" >&2
  exit 1
fi

ENTRY="$(basename "$CONFIG" .runtimeconfig.json)"

# Change INTO the directory, not only start the DLL from there: ASP.NET's
# content root is the WORKING DIRECTORY. From /app every service read an
# /app/appsettings.json that does not exist — and the gateway died on its
# missing ocelot.json. Measured at the first `compose up`, not assumed.
cd "$DIRECTORY"

echo "==> ${SERVICE_DIR}: dotnet ${ENTRY}.dll"
exec dotnet "${ENTRY}.dll" "$@"
