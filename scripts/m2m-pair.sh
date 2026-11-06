#!/usr/bin/env bash
# The service clients and how a pair comes about (ADR-0057). Sourced by
# scripts/env-m2m.sh and scripts/k8s-up.sh, so that there is one list and one
# recipe. The recipe is Noelia's (its ADR-0002, section 4): 32 random bytes as
# base64url, stored as sha256 of those 43 characters.

M2M_CLIENTS="advisor applications assessment companies jobs messages notification profile resume scout transfer"

m2m_b64url() {
  openssl base64 -A | tr '+/' '-_' | tr -d '='
}

# Prints the secret on the first line and its hash on the second.
m2m_pair() {
  local secret
  secret=$(openssl rand 32 | m2m_b64url)
  printf '%s\n' "$secret"
  printf 'sha256:%s\n' "$(printf %s "$secret" | openssl dgst -sha256 -binary | m2m_b64url)"
}
