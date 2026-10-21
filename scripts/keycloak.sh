#!/usr/bin/env bash
# A local identity provider for the Noelia Control Plane: Keycloak on
# https://localhost:8443, realm "workertransfer" (VISION-FRAGEN-2 Q4).
#
#   make keycloak          # start, and set up realm, client, group, operator
#   make keycloak-down     # stop — the realm stays in its volume
#
# ---------------------------------------------------------------------------
# WHY HTTPS ON A LAPTOP
#
# The control plane refuses an authority that is not https, in EVERY
# environment (`AccessOptions.Validate` in NoeliaControlPlane) — Development
# included. So Keycloak serves TLS with the .NET development certificate for
# `localhost`, and that certificate has to be trusted by macOS: the control
# plane fetches the discovery document from the host, and .NET asks the
# keychain. Trusting it is a change to the keychain and is the person's step,
# never this script's: `dotnet dev-certs https --trust`.
#
# WHY NOT IN docker-compose.yml
#
# For the reason the control plane is not there either: `make up` must not
# depend on what only an operator of the control plane needs.
#
# THE SECRETS
#
# The admin password, the client secret and the operator's password are rolled
# once and kept in .control-plane/keycloak.env (git-ignored, mode 600). They
# never stand in an argument list — the container reads them from an env file,
# and this script prints where they are, never what they are.
set -euo pipefail

cd "$(dirname "$0")/.."

red()   { printf '\033[31m%s\033[0m\n' "$*"; }
green() { printf '\033[32m%s\033[0m\n' "$*"; }
grey()  { printf '\033[2m%s\033[0m\n' "$*"; }

# Pinned, and not the newest: a release gets a few weeks before it carries an
# operator's sign-in.
IMAGE=quay.io/keycloak/keycloak:26.7.5
NAME=workertransfer-keycloak
VOLUME=workertransfer-keycloak-data
PORT=8443
REALM=workertransfer
CLIENT=noelia-control-plane
GROUP=admins
STATE="$PWD/.control-plane"
SECRETS="$STATE/keycloak.env"
TLS="$STATE/keycloak-tls"

if [[ "${1:-}" == "--down" ]]; then
  docker rm -f "$NAME" >/dev/null 2>&1 && grey "stopped $NAME (the realm stays in volume $VOLUME)" || true
  exit 0
fi

command -v docker >/dev/null || { red "docker is missing."; exit 1; }
command -v dotnet >/dev/null || { red "dotnet is missing (for the development certificate)."; exit 1; }

if ! dotnet dev-certs https --check --trust >/dev/null 2>&1; then
  red "The .NET development certificate is not trusted on this machine."
  echo "The control plane only accepts an https authority, and it checks it against the keychain."
  echo "Trust it once (macOS asks for your password):"
  echo
  echo "    dotnet dev-certs https --trust"
  echo
  exit 1
fi

mkdir -p "$STATE" "$TLS"

# The certificate for localhost, as PEM for Keycloak. Readable by the
# container's user — it is the development certificate, valid for localhost
# only, and the directory is git-ignored.
if [[ ! -f "$TLS/cert.pem" || ! -f "$TLS/cert.key" ]]; then
  dotnet dev-certs https --export-path "$TLS/cert.pem" --format Pem --no-password >/dev/null
  chmod 644 "$TLS/cert.pem" "$TLS/cert.key"
fi

# The realm lives in the volume, its secrets in this file — and they belong
# together. Rolling new ones over an existing realm would leave a client
# secret and passwords that nothing in the realm knows.
if [[ ! -f "$SECRETS" ]] && docker volume inspect "$VOLUME" >/dev/null 2>&1; then
  red "Volume $VOLUME holds a realm, but ${SECRETS#$PWD/} is missing."
  echo "Either bring the file back, or start over — that deletes the local realm:"
  echo
  echo "    make keycloak-down && docker volume rm $VOLUME && make keycloak"
  echo
  exit 1
fi

if [[ ! -f "$SECRETS" ]]; then
  roll() { openssl rand -base64 24 | tr -d '/+=' | cut -c1-24; }
  umask 077
  {
    echo "KC_BOOTSTRAP_ADMIN_USERNAME=admin"
    echo "KC_BOOTSTRAP_ADMIN_PASSWORD=$(roll)"
    echo "CONTROL_PLANE_CLIENT_SECRET=$(roll)"
    echo "CONTROL_PLANE_OPERATOR_PASSWORD=$(roll)"
  } >"$SECRETS"
  grey "rolled the three secrets into ${SECRETS#$PWD/}"
fi

if lsof -nP -iTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1 \
   && ! docker ps --format '{{.Names}}' | grep -qx "$NAME"; then
  red "Port $PORT is taken by something else:"
  lsof -nP -iTCP:"$PORT" -sTCP:LISTEN | tail -n +2
  exit 1
fi

if ! docker ps --format '{{.Names}}' | grep -qx "$NAME"; then
  docker rm -f "$NAME" >/dev/null 2>&1 || true
  grey "starting $IMAGE"
  docker run -d --name "$NAME" \
    -p "127.0.0.1:$PORT:8443" \
    -v "$VOLUME:/opt/keycloak/data" \
    -v "$TLS:/tls:ro" \
    --env-file "$SECRETS" \
    "$IMAGE" start-dev \
      --https-port=8443 \
      --https-certificate-file=/tls/cert.pem \
      --https-certificate-key-file=/tls/cert.key \
      --hostname="https://localhost:$PORT" >/dev/null
fi

# Ready means the discovery document answers over TLS that verifies — against
# the exported certificate, not with -k: an unverified probe would be green
# over exactly the failure this setup exists to avoid.
discovery() {
  curl -fsS --max-time 3 --cacert "$TLS/cert.pem" \
    "https://localhost:$PORT/realms/$1/.well-known/openid-configuration" >/dev/null 2>&1
}
for _ in $(seq 1 90); do
  discovery master && break
  docker ps --format '{{.Names}}' | grep -qx "$NAME" || {
    red "Keycloak exited — last lines:"; docker logs --tail 15 "$NAME" 2>&1; exit 1; }
  sleep 1
done
discovery master || { red "Keycloak does not answer on https://localhost:$PORT"; exit 1; }

# Everything below runs inside the container, which already holds the admin
# password in its environment; the two other secrets travel in an env file.
kc() { docker exec --env-file "$SECRETS" "$NAME" bash -c "$1"; }

kc '/opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 \
      --realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null 2>&1'

if kc "/opt/keycloak/bin/kcadm.sh get realms/$REALM >/dev/null 2>&1"; then
  grey "realm $REALM already set up"
else
  grey "setting up realm $REALM"
  kc "set -e
    K=/opt/keycloak/bin/kcadm.sh
    \$K create realms -s realm=$REALM -s enabled=true -s displayName='WorkerTransfer Betrieb' >/dev/null

    # A confidential client with the code flow and PKCE — what the control
    # plane asks for (Access.cs). No direct password grant: nobody types a
    # password into anything but Keycloak's own page.
    CID=\$(\$K create clients -r $REALM -i \
      -s clientId=$CLIENT -s enabled=true -s publicClient=false \
      -s secret=\"\$CONTROL_PLANE_CLIENT_SECRET\" \
      -s standardFlowEnabled=true -s directAccessGrantsEnabled=false \
      -s 'redirectUris=[\"http://localhost:8091/signin-oidc\"]' \
      -s 'attributes={\"pkce.code.challenge.method\":\"S256\",\"post.logout.redirect.uris\":\"http://localhost:8091/*\"}')

    # The group travels as the claim \"groups\", by name and not by path:
    # the control plane maps exactly that string (RoleMap).
    \$K create clients/\$CID/protocol-mappers/models -r $REALM \
      -s name=groups -s protocol=openid-connect -s protocolMapper=oidc-group-membership-mapper \
      -s 'config={\"claim.name\":\"groups\",\"full.path\":\"false\",\"id.token.claim\":\"true\",\"access.token.claim\":\"true\",\"userinfo.token.claim\":\"true\"}' >/dev/null

    GID=\$(\$K create groups -r $REALM -i -s name=$GROUP)

    # A complete profile, or Keycloak asks for one at the first sign-in.
    UID_=\$(\$K create users -r $REALM -i -s username=operator -s enabled=true \
      -s email=operator@workertransfer.test -s emailVerified=true \
      -s firstName=Lokaler -s lastName=Betrieb)
    \$K set-password -r $REALM --userid \$UID_ --new-password \"\$CONTROL_PLANE_OPERATOR_PASSWORD\"
    \$K update users/\$UID_/groups/\$GID -r $REALM -s realm=$REALM -s userId=\$UID_ -s groupId=\$GID -n
  "
fi

discovery "$REALM" || { red "Realm $REALM has no discovery document."; exit 1; }

green "Keycloak on https://localhost:$PORT — realm $REALM, client $CLIENT, group $GROUP → admin"
grey  "Sign in as 'operator'; the password is CONTROL_PLANE_OPERATOR_PASSWORD in ${SECRETS#$PWD/}."
grey  "Admin console: https://localhost:$PORT/admin (KC_BOOTSTRAP_ADMIN_* in the same file)."
