# Skill: Consent-Ledger (consent as an enabler, not a feature)

## Purpose
Make consent the gate behind every visibility, send, contact, and data-import
capability. The Consent-Ledger is append-only and authoritative; no feature
proceeds without a checked, currently-valid consent entry.

## When to use
Whenever a feature exposes a candidate's data, sends something on their behalf,
or imports external data. Phases 3, 4, 5, 6, 8 all touch consent.

## Hard rules (from `docs/product-scope.md` and ADR-0004)
1. **Consent is checked, never assumed.** A feature that touches candidate data
   calls the Consent-Ledger first; absence or revocation blocks the action.
2. **Append-only ledger.** Grant/revoke/delete are events, not in-place edits.
   Revocation deletes/obscures derived artifacts (scores, generated docs).
3. **Granular and scoped.** A consent entry names: purpose, recipient, scope of
   data, and source (e.g. GitHub import: which repos, which scopes). Not a
   single "yes to everything".
4. **Revocable and deletable at any time**, and revocation takes effect
   immediately — no eventual window where revoked data is still reachable.
5. **User-owned.** The candidate controls their ledger; employers cannot grant
   consent over candidate data.

## Pattern
```python
async def can_contact(candidate_id, company_id, purpose) -> ConsentDecision:
    ledger = await consent_ledger.active(candidate_id, company_id, purpose)
    return ledger  # .granted / .revoked / .scopes

# every contact/send/import path:
decision = await can_contact(...)
if not decision.granted:
    raise ConsentRequired(...)   # NEVER silently continue
```

## Tests (mandatory)
- Feature fails when consent is absent.
- Feature fails when consent was granted then revoked.
- Revocation removes or hides the derived artifacts built under that consent.
- Scopes matter: consent for one purpose/source does not authorize another.
