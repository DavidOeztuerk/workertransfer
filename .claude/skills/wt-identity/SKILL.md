---
name: wt-identity
description: Use when working on who a caller is in WorkerTransfer — identity-service, tokens and their exact claim set, the `access` cookie, tenant and `Capacity` (person vs. company), switching capacity (`/auth/company/{id}`, `/auth/self`), registration and company creation, invitations, the roles `member` < `recruiter` < `admin` and which routes need which, the internal role door, or signing in with Google, Microsoft or Apple.
---

# Request context, roles and sign-in

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liners stay there.

- `docs/adr/0053-anmelden-mit-fremden-konten.md` — signing in with Google, Microsoft, Apple; never linked by the address. 18.10.2026.

### Request context

Correlation and tenant flow through the request, set by middleware in `ServiceDefaults`. **Tenant identity must never come from a browser header in production.**

**A tenant is a company, and a natural person has none** (ADR-0017). This is modelled as `Capacity`: `AsSelf` or `ForCompany(TenantId)`. Tenant is an *optional* attribute of a principal, carried only by company-based features (adverts, employer accounts, recruiting teams). It is **not** the scoping axis for personal data: user data is scoped by subject identity, and both axes coexist. The consent ledger therefore has no tenant column by design — a consent belongs to the person and follows them across employers. Do not "fix" that by adding one.

Company membership is a relation, not a column on the user — one person may act for several companies (ADR-0018). Email is globally unique. `POST /auth/login` returns a person token with **no** tenant claim; `POST /auth/company/{id}` verifies membership and only then mints a token carrying the tenant. So the client names the company but the server decides, and the tenant in the token never came from client input. `null` means "acted as a person", not "missing".

**The way back is `POST /auth/self`** (20.10.2026) — it only gives rights away, so it asks no membership, but it still issues a token: a person token cannot be computed out of a company one. Its session remembers no company, which refresh reads as "oneself". The browser remembers the last choice per account (`features/auth/lib/capacityMemory.ts`) and restores it **once per sign-in, silently**; without a choice, whoever has exactly one company as `admin` — who registered it — starts acting for it. A stale choice costs one refused switch and is forgotten. That memory is a convenience, never a claim: the server checks the membership on every switch.

The access token carries `sub`, `email`, `jti`, `iat`, `exp`, `iss`, `aud`, `session_id`, the long-form `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` (a duplicate of `sub`), and — only while acting for a company — `tenant`. **Nothing else.** During the migration it also carried `tenant_id` and `type`; both are gone.

That ninth claim was undocumented until it was **measured against the running stack on 03.09.2026** — this file said "eight, nothing else", the wire said nine. It stays, and the reason is Noelia's: `MapInboundClaims = false` switches off the framework's own derivation, and seventeen readers resolve the caller through that name, two of them in provider packages that cannot see Noelia's assembly. Dropping it saves seventy bytes and turns every one of those into a silent `null`.

The lesson is the test, not the claim: `TokenShapeTests` forbade two *names* and would never have noticed a ninth. It now pins the **complete set** in both capacities — a set comparison, not a membership check. Roles and permissions are not in the token: they are read from the membership table per operation, so the token was never authoritative.

**Two suites, because one of them could not have caught it.** `TokenShapeTests` calls Noelia's issuer directly; `TokenShapeAtTheWireTests` goes the whole way — register, confirm, sign in, switch to a company — and reads the token where a browser gets it, out of the `Set-Cookie`. Between the issuer and the wire sit the service's settings, its wiring and the cookie, and a claim added by *that* layer would be invisible to the first suite. The endpoint suite also pins something the unit suite cannot: **the token never appears in a response body.** It leaves only as the `httpOnly` cookie, so no script in the browser can read it, and there is no second path to the same string for someone to log by accident. Re-measured at the running stack on 04.09.2026: nine as a person, ten for a company, both matching.

The JWT handler resolves the principal from an `Authorization: Bearer` header **or** the `access` cookie, in that order — the cookie half is `AccessCookie.AlsoFromTheCookie()` in `ServiceDefaults`. Both carriers are needed: service-to-service and CLI callers send the header; the browser never sees the `httpOnly` token and can only replay it as a cookie. Any new service verifying identity tokens must accept both.

**A company is created at registration and nowhere else.** `POST /register` takes an optional company name; the intent is stored on the pending user and redeemed by `POST /auth/verify-email`, in the same transaction. The order is forced, not chosen: creating a company requires an *active* account and derives the domain from the **confirmed** address, so it is proven before the company exists and cannot be forged (ADR-0019).

Two checks sit in two places on purpose. **Freemail is rejected at registration**, and **before** the existence check — otherwise a *known* freemail address would get the silent "ok" and an unknown one a 422, and that difference is exactly the enumeration channel `/auth/register` closes. **"Domain already claimed" is checked only at confirmation**: earlier it would answer *"is firma.de on this platform?"* for anyone who guesses a domain. That creates a state which did not exist before — **account confirmed, company refused** — and `/verify` says so; the confirmation itself must never fail because a name was taken.

**There is no "create a company" button and no `/company/new` route.** Someone already registered as a person joins a company by **invitation**, which is also the right answer when their domain is already claimed — they have colleagues there.

**`admin` vs `member` is enforced since PBI-2, and until then it was not — anywhere.** The tree held **zero** `RequirePermission` outside identity-service: the navigation *hid* company entries and the server answered 403 only where somebody had thought of it. `MembershipPermission` read the role per request from the membership table — and nobody asked. **Hiding is not access control**, and that sentence is the whole reason this was the largest real gap in the tree.

Nineteen routes now demand an `admin` — eight since PBI-2, four since ADR-0050, four since ADR-0052, three since the company's own AI key — and the line behind them is written down once so the next route does not reinvent it: **`admin` is whoever binds or changes the company, or who belongs to it; `member` is the daily work in the company's name** — reading, drafting, writing, approaching, processing applications. **In doubt, `member`:** a right that is too narrow turns an invitation into a spectator seat, and then somebody creates a second admin in order to work — and "admin" is a word in a table again.

| route | why `admin` |
|---|---|
| `POST /companies/{id}/invitations` | who belongs to the company |
| `DELETE /companies/{id}/invitations/{id}` | the reverse side of inviting |
| `DELETE /companies/{id}/members/{id}` | who belongs to the company |
| `POST /jobs/{id}/publish` | the advert starts representing the company |
| `POST /jobs/{id}/close` | it stops — and with it the chance to apply |
| `PUT /companies/me/profile` | the shop window, public and for everyone |
| `POST /transfers/{id}/offer` | the start date |
| `POST /transfers/{id}/complete` | states that it holds |
| `PUT /transfers/employer/setting` | lets other companies ask about the own staff (ADR-0050) |
| `POST /transfers/{id}/release`, `/transfers/releases/{id}/terms`, `/accept` | a release month and a fee — one company pays, the other lets somebody go sooner |
| `POST /jobs/sources`, `PUT`/`DELETE /jobs/sources/{id}`, `POST /jobs/sources/{id}/fetch` | a source in "direct" publishes in the company's name (ADR-0052) |
| `GET`/`PUT /companies/{id}/ai`, `PUT /companies/{id}/ai/key` | where the team's drafts go and who pays for them (ADR-0051 Ergänzung) — reading included |

**A third role since 07.10.2026: `recruiter`** ([ADR-0048](docs/adr/0048-der-recruiter-macht-den-ersten-schritt.md), VISION-FRAGEN B7). The roles are **ordered**, `member` < `recruiter` < `admin`, and `ServiceDefaults.Roles.CompanyPermissions` names a *minimum* per permission (`role >= minimum`) — so an admin may approach too, and a fourth role must be inserted where it belongs in `CompanyRole` or it silently gains or loses rights. The recruiter's one addition is the **first contact** to a person who did not come forward: `POST /resumes/{subject}/requests`, `POST /market/{subject}/requests`, `POST /advisor/conversations`. Searching stays `member`. resume-service therefore needs `Identity__BaseUrl`/`Identity__Secret` now. In the UI the 403 reads `errors.recruiterOnly` via `RECRUITER_CASES`, never "choose a company".

Three of those already had the check in the *handler*; moving it to the endpoint moved it **before** the body is looked at — otherwise a stranger learns from the difference 400/403 that their body was fine.

**Where the role comes from is the part that carries the design.** Not the token: a token outlives the removal, and the removal would then take effect on expiry — at exactly the operation where *immediately* is the only thing that counts. identity-service answers `GET /internal/companies/{id}/members/{sub}/role` behind the shared secret; `ServiceDefaults.Roles.AdminPermission` asks it per protected request, with no cache, for the same reason a consent must take effect on the next read (ADR-0013). The price is one lookup per protected request, and it touches only the operations that bind a company — never the reads people use all day.

**Two things that are easy to get wrong here.** The tenant comes from the **token** in the ten services (`/jobs/{id}/publish` names no company) and from the **path** in identity (`/companies/{id}/…`) — that is why there are two handlers and not one. And **a silent role lookup answers 503, never 403**: the handler can only say "yes", so it leaves a note on the `HttpContext` and `RejectionShape` turns the refusal into 503. A 403 would read as "your right was taken away", and nobody would go looking for an outage.

**`Identity__BaseUrl` and `Identity__Secret` are therefore load-bearing configuration** in jobs, companies and transfer (applications and notification already had them), and in scout for the company's AI key. Without them every protected route answers 503 — correctly, and visibly.

Do not mistake the navigation for access control.

**Registration is open to any email address, private ones very much included** — the transfer market's normal user is a person with no company. The freemail blocklist applies at exactly one place: claiming a domain. Accounts start pending and are activated by a mailed token; register and resend answer identically whether or not the address is known, because a differing answer would reveal platform membership without asking the ledger.

### Signing in with Google, Microsoft, Apple: identity-service itself, never linked by the address

Built 18.10.2026 under [ADR-0053](docs/adr/0053-anmelden-mit-fremden-konten.md) (VISION-FRAGEN-2 N3). identity-service is the relying party itself (code, PKCE, nonce); Noelia has nothing for it.

- **Linked by the provider's id (`external_logins`: provider, `sub`, account), never by the address.** An unknown id whose proven address already has an account is *not* linked — otherwise whoever can enter a foreign address at a provider takes that account over; binding happens only signed in, under settings. **Microsoft vouches for no address** (measured: no `email_verified`, the issuer is per tenant), so a Microsoft account gets our confirmation mail and a known address the registration's "somebody tried" mail — the same answer either way.
- **The callback runs through the service**, because Apple answers with a form post. State, verifier, nonce and intent ride in a sealed, short-lived `external_flow` cookie (`SameSite=None; Secure` for Apple), so a flow finishes only in the browser that started it.
- **The exchange is a query, the decision a command**; the freshly created account is signed in from the aggregate, not re-read — inside the transaction the row is not written yet.
- **No token is kept, no provider is audited** (`AuditEvent.AllowedMetadata`); an account made through a provider has no password entry (`SignInHandler` passes it as null), and its last way in cannot be taken off (409).
- **Empty client ids mean off** — `GET /auth/external` lists only configured providers, `start` and `callback` of an unconfigured one are 404.
