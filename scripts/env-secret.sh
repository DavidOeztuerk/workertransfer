#!/usr/bin/env bash
# Puts a provider credential into .env WITHOUT it ever reaching a screen, a log
# or a chat: the value travels from a downloaded file or the clipboard straight
# into the file, and what is printed is the key and a length — never the value.
#
#   scripts/env-secret.sh --clipboard KEY
#       the value someone just copied in a provider's console (pbpaste)
#   scripts/env-secret.sh --stdin KEY
#       a public identifier read off a page (a client ID, an app ID)
#   scripts/env-secret.sh --file KEY path/to/file
#       the file as the value — Apple's .p8 key is stored as its base64 body
#   scripts/env-secret.sh --google-json ID_KEY SECRET_KEY path/to/client_secret.json
#       Google's downloaded OAuth client: client_id and client_secret
#
# A key that already stands in .env is replaced in place, otherwise appended.
# The downloaded file is NOT deleted: it is the person's file, and the line
# printed at the end says where it still lies.
set -euo pipefail

cd "$(dirname "$0")/.."
ENV_FILE=.env

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Keine .env — zuerst 'make env'." >&2
  exit 1
fi

# Writes KEY=value through Python so no value stands in an argument list,
# where `ps` could read it. The value arrives on stdin.
write() {
  local key="$1"
  if [[ ! "$key" =~ ^WORKERTRANSFER_[A-Z0-9_]+$ ]]; then
    echo "Unbekannter Schlüssel '$key' — erwartet WORKERTRANSFER_…" >&2
    exit 1
  fi
  python3 -I -c '
import sys, re
key, path = sys.argv[1], sys.argv[2]
value = sys.stdin.read().strip("\r\n")
if not value:
    sys.exit("Leerer Wert für " + key + " — nichts geschrieben.")
if "-----BEGIN" in value:
    # A PEM key (Apple .p8): its base64 body on one line, which is what the
    # identity service reads (`Pem` in ExternalLogin.cs) — a literal "\\n"
    # would not be split there and the key would never import.
    value = "".join(l.strip() for l in value.splitlines() if l.strip() and not l.startswith("-----"))
elif "\n" in value:
    sys.exit("Mehrzeiliger Wert für " + key + " — .env hält eine Zeile.")
lines = open(path, encoding="utf-8").read().splitlines()
line = f"{key}={value}"
for i, existing in enumerate(lines):
    if re.match(rf"^{re.escape(key)}=", existing):
        lines[i] = line
        break
else:
    lines.append(line)
open(path, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print(f"{key} gesetzt ({len(value)} Zeichen).")
' "$key" "$ENV_FILE"
}

case "${1:-}" in
  --clipboard)
    [[ $# -eq 2 ]] || { echo "Aufruf: $0 --clipboard KEY" >&2; exit 2; }
    pbpaste | write "$2"
    echo "Tipp: die Zwischenablage hält den Wert noch — kopier etwas anderes darüber."
    ;;
  --stdin)
    [[ $# -eq 2 ]] || { echo "Aufruf: $0 --stdin KEY" >&2; exit 2; }
    write "$2"
    ;;
  --file)
    [[ $# -eq 3 ]] || { echo "Aufruf: $0 --file KEY DATEI" >&2; exit 2; }
    [[ -f "$3" ]] || { echo "Datei fehlt: $3" >&2; exit 1; }
    write "$2" < "$3"
    echo "Die Datei liegt noch unter $3 — lösch sie, sobald die .env stimmt."
    ;;
  --google-json)
    [[ $# -eq 4 ]] || { echo "Aufruf: $0 --google-json ID_KEY SECRET_KEY DATEI" >&2; exit 2; }
    [[ -f "$4" ]] || { echo "Datei fehlt: $4" >&2; exit 1; }
    for field in client_id client_secret; do
      key=$([[ $field == client_id ]] && echo "$2" || echo "$3")
      python3 -I -c '
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8"))
client = data.get("web") or data.get("installed") or {}
value = client.get(sys.argv[2], "")
if not value:
    sys.exit("Kein " + sys.argv[2] + " in der Datei — ist es ein OAuth-Client vom Typ Webanwendung?")
sys.stdout.write(value)
' "$4" "$field" | write "$key"
    done
    echo "Die Datei liegt noch unter $4 — lösch sie, sobald die .env stimmt."
    ;;
  *)
    sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
    ;;
esac

echo "Wirksam nach: docker compose up -d identity-service web"
