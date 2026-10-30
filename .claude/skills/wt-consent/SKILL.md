---
name: wt-consent
description: Use when touching WorkerTransfer's consent ledger, visibility or account erasure — consent-service and its endpoints (`/check`, `/check-batch`, `/consent/me`), anything that reads a capability before showing data, the 404/403/503 meanings in profile- and resume-service, resume requests, certificates' own capability, `POST /account/erasure` and its cascade, `ErasureRecipient.Services`, `Retention.*`, or the `/delete-account` page.
---

# Consent, visibility, erasure

Moved verbatim from `CLAUDE.md` (30.10.2026). Each rule also stands as one binding
line in `CLAUDE.md`, „The rules that carry the design".

### Consent is read synchronously and never cached (ADR-0013)

`consent-service` is the enabler everything else leans on: `POST /consent/{grant,revoke,check,check-batch}`, `GET /consent/me`, `GET /consent/me/history`. A withdrawal has to take effect on the very next read, so a cache here is not a performance detail but a rule violation.

**`/check-batch` changes nothing about that** (ADR-0030): same synchronous read, no cache, no reason field — one round trip instead of forty. It exists because a candidate page measured 1.7–8.8 s and hung the UI; afterwards 0.015–0.083 s with byte-identical results. Answers come back **in the order of the questions**, and a caller refuses a mismatched length rather than guessing — misaligning them would show the wrong person's profile.

`/check` deliberately answers **without** the withdrawal reason: the reason is free text a person wrote about themselves and must not ride along on a query any authenticated caller may issue. `GET /consent/me` is the opposite case and takes **no** subject id at all, in path or query — a foreign list would say which *other* companies hold access.

**There is no `POST /consent/delete`** (ADR-0027 §1). It was *capability*-scoped, every capability in this system is a *visibility*, and "delete `profile.visibility:public`" therefore could never mean "delete the profile" — equating the two would erase a CV on a visibility withdrawal nobody asked for. The DELETE action stays and has exactly one producer: account erasure.

### Visibility lives in the ledger, nowhere else (ADR-0020)

`profile-service` has **no visibility field**. Its status codes carry the design and are easy to break by tidying:

- **404** means *hidden or non-existent*, and the two must stay byte-identical apart from the correlation id.
- **403** means *no active company* — a statement about the caller, which leaks nothing.
- **503** means *the ledger is silent* — neither 404 nor showing the profile, because both would assert something nobody knows.

`resume-service` is the same idea one step stricter. A profile is a notice board; a CV names real employers with dates — exactly what a current employer must not see. So there is **no public switch**: a company asks, the person answers, and the release covers that one company. The load-bearing distinction is that **the request is not the permission**: granted means "was granted once", not "holds now", so a resume request has neither an active flag nor a revoked timestamp. After a withdrawal the request stays granted and the read still comes up empty — that is the design, not a bug to fix. Asking requires the *profile* release, never the existence of a CV: "has already written one" is a fact about the person nobody should be able to probe for.

Certificates ride a capability of their own: `documents.visibility:tenant:<id>` (ADR-0035). They are not folded under `resume` — a career history is self-written text, a certificate is a third-party document with names and grades, and whoever wants to show one without the other must be able to.

### Erasure: the default deletes completely (ADR-0027)

`POST /account/erasure` at identity-service — self only, **no reason field**: demanding a justification from someone who wants to leave is a lever against them. It immediately revokes every session and disables the account, then cascades over the **outbox** to twelve recipients (`consent`, `profile`, `resume`, `portfolio`, `applications`, `transfer`, `github`, `notification`, `scout`, `advisor`, `assessment`, `messages` — `ErasureRecipient.Services`) and identity itself, last. `jobs-service` and `companies-service` are **not** recipients — they hold nothing about a natural person, and `ErasureRecipientTests` goes red the moment any service grows a personal table and is not on the list. That guard reads the **EF model**, not the source, and uses two signals: personal column names *and* the `SubjectKeyedTable` annotation.

Three things are load-bearing:

- **The default deletes everything — including hired applications and paid transfers.** The retention exception is one named constant per affected service (`Retention.KeepHired`, `Retention.KeepPaid`), it is `false`, and it is a **constant, not a setting**: with a deletion promise, "different in production than in test" is the worst possible state. It is also `static readonly` rather than `const`, because a `const` is inlined into the calling assembly and a reverted counter-probe then goes unnoticed. Tests pin the value *and* pin that the flipped switch covers exactly two row classes. Do not widen it.
- **The erasure delivery must be able to fail.** It raises on transport error *and* on non-2xx, where the ordinary notifier swallows both — right for a mail, and here it would make the delivery timestamp a lie. Erasure runs with **no attempt ceiling** and growing backoff. A permanently dead recipient therefore *blocks* completion — that is the point, and no timeout may "finish" it.
- **The order is enforced, not advised:** every acknowledgement → final mail → only then the user row falls. `NotYetException` expresses "not yet" without spending an attempt. The final notice says only *that* it is done; listing what was deleted would copy the data into an inbox that may not be the person's alone.

`/delete-account` is where the promise is made to a person: it states **before** the button what disappears, that it is **not immediate**, and the uncomfortable part out loud — *the application you were hired through disappears from the company's list too*. It promises no exception, because in the default there is none. Confirmation is two deliberate steps with a differently worded second button — no typed word, no cooling-off period, no re-entered password: whoever wants to delete may. Afterwards it says "läuft", never "erledigt", and shows no progress bar. One trap: the page clears the session on success, so the shell re-renders it with no principal — the accepted state must take precedence over the login prompt, or the person sees "Bitte anmelden" right after deleting their account.

The ledger survives as the **proof**: the grant/revoke chain stays, one DELETE row per capability ever held is appended, and reasons and metadata are cleared. The argument is not "we may retain" but that nothing maps subject id → person afterwards.

## Tables keyed by the person (from `### Persistence`)

`SubjectKeyedTable` is an EF annotation put on tables whose *key* is the person (`profiles`, `portfolios`, `github_connections`, `resumes`, `notification_preferences`, `market_status`). It exists so the erasure guard can recognise them: those tables have no `subject_id` column, because the id *is* the subject, and a guard looking only for column names would miss exactly the tables that hold the most.
