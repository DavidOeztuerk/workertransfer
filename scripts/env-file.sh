#!/usr/bin/env bash
# Reading and writing single keys of a .env. Sourced by scripts/env-m2m.sh and
# scripts/env-services.sh; both set `file` first. No value is ever printed.

value_of() {
  awk -F= -v k="$1" '$1 == k { sub(/^[^=]*=/, ""); print; exit }' "$file"
}

set_value() {
  local key="$1" value="$2" tmp
  tmp=$(mktemp)
  if grep -q "^${key}=" "$file"; then
    awk -v k="$key" -v v="$value" 'index($0, k "=") == 1 { print k "=" v; next } { print }' "$file" > "$tmp"
  else
    cat "$file" > "$tmp"
    # Without a last newline the key would land on the value of the last line.
    if [ -n "$(tail -c1 "$tmp")" ]; then
      printf '\n' >> "$tmp"
    fi
    printf '%s=%s\n' "$key" "$value" >> "$tmp"
  fi
  cat "$tmp" > "$file"
  rm -f "$tmp"
}
