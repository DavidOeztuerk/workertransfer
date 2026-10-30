---
name: wt-github
description: Use when working on WorkerTransfer's github-service or GitHub evidence on the profile page — the required `User-Agent`, the gist and OAuth proofs, `POST /me/oauth/start` and `finish`, `GET /github/oauth`, what evidence may be stored (topics and the language set, never byte counts), or turning evidence into a skill suggestion.
---

# github-service

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there.

### github-service: the header that made it never work

**Every request needs a `User-Agent`.** GitHub answers *everything* without one with `403` — not a 400, not a message naming the header. Measured 04.09.2026 against the same address: with the header `200`, without it `403`. While it was missing, this service had never worked: neither the gist proof nor the repository fetch, and both surfaced as "github unavailable", which reads exactly like an outage *at GitHub*. `HttpGitHubTests.Every_request_carries_a_user_agent` pins it.

**Evidence carries topics and the language SET, never the bytes.** GitHub reports a byte count per language; that is precisely what the deleted package turned into "skill" as `bytes / total_bytes` (ADR-0022 §2). The names are a fact about a repository, the numbers would be the raw material for a statement about a person — so they are not stored, and a test serialises a fetched repository to prove the numbers do not travel even as text. Topics are the strongest evidence of all because they are a *naming*: a human wrote "kubernetes" onto that repository.

**Evidence becomes a suggestion, never a claim.** The profile page offers those words under the skills field; one click fills the form, and only *Save* makes it a naming. That is the bridge the scout needs — the search index knows only what a person typed — and it is two deliberate acts so neither happens by accident.

**Proof runs over OAuth when configured, over a gist otherwise.** `GitHub__OAuth__*` empty means the gist stays, and the button does not appear.

**OAuth needs no name typed first, and that correction was measured.** The naming step was the *gist's* requirement wearing the wrong hat: a gist search must know whose gists to look in. GitHub reports only the account that actually granted consent, so there is nothing to compare and no foreign account to slip in. Measured 05.09.2026 against a real account: the step protected nobody and locked out the person whose connection still carried an older name (the sample data's `sindresorhus`), answering 422 on a correct authorisation. So `POST /me/oauth/start` now *creates* a connection with `Login = null` — "not named yet", a state and not a gap — and `finish` writes in the login GitHub reports. **Where a name was typed it is still compared**, unchanged and for the old reason: otherwise someone names `torvalds`, authorises themselves, and walks away with a proof for a foreign account. Two tests hold the two halves apart, and the comparison has a counter-probe.

`start` is a *command*, not a query, precisely because it creates that row — a query would run outside `TransactionBehavior` and the row would never commit. It is therefore called **on click, never on page load**: prefetching it would leave a row for everyone who merely looked at the page. What the page *may* ask on load is `GET /github/oauth` → `{"available": bool}`, a statement about this server's configuration that holds nothing personal and creates nothing.

The one-time string already on the connection is the OAuth `state`. **No scope is requested and the access token is not kept**: it is needed for a single `GET /user`, and what you do not store you cannot lose.
