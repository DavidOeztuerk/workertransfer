---
name: wt-gateway
description: Use when touching WorkerTransfer's gateway or its route map — `src/gateway`, `ocelot.json`, route order and priorities, the auth brake (rate limits on `/auth/*`, `WORKERTRANSFER_BRAKE_FACTOR`, a surprising `X-RateLimit-Limit`), `X-Forwarded-For`, CORS origins, `docs/routenkarte.yml`, `RouteMapTests`, `make route-map`, or adding an endpoint that must appear in the map.
---

# Gateway, brake and route map

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liners stay there.

### The brake on the auth endpoints, and why it lives in the gateway

Five paths are braked — `/auth/login` (20/min), `/auth/register` (5), `/auth/resend-verification` (3), `/auth/verify-email` (20), `/auth/refresh` (60) — per origin, per minute. The rules live in `ocelot.json` next to the routes they select from, as Noelia's own `DistributedRateLimiting` section; the middleware is **Noelia's**, and there is no hand-written brake here any more.

**The three defaults are `0`, and that is the whole trick.** A limit of zero writes no counter, so only the five named paths count. Without it a default would apply to every path — and the whole UI travels through this gateway, so each asset fetch would count. `BrakeTests` pins both: the five paths, and that the defaults stay at zero.

**`WORKERTRANSFER_BRAKE_FACTOR` multiplies every limit, and it is why a measurement can look broken.** Measured 09.09.2026: six calls to `/auth/register` (limit 5) all came back `201`, and the answer carried `X-RateLimit-Limit: 200` — a number that appears nowhere in `ocelot.json`. It is `5 × 40`: `.env` sets the factor to 40 so an E2E run does not brake itself. **A factor and not a switch, deliberately** — the limiter still counts, still keys per origin, still answers with its own headers, so what is measured is the real path and not a disabled one. With the factor at `1` the promise holds exactly: three through, the fourth `429` with `X-RateLimit-Limit: 3`, `X-RateLimit-Remaining: 0`, `Retry-After: 60`, an RFC 9457 body, and the supplied correlation id in **both** header and body.

Two suites, because one could not have found this: `BrakeTests` asserts each braked path has a route — a statement about the *selection*. `BrakeBindingTests` binds the real `ocelot.json` to Noelia's options type and asserts what arrives — the *binding*. A configuration that never lands still has perfectly valid paths.

**Per origin, never per email address.** Keying on the address would build exactly the enumeration channel `/auth/register` closes: a braked answer would confirm the address exists. It would also let a stranger lock out anyone whose address they know. The key is path plus origin, and the brake never reads the body — a test sends five *different* addresses from one origin and requires them to share one bucket.

**In the gateway, and that was measured rather than chosen.** A brake needs the caller's origin, which lives in `Connection.RemoteIpAddress`. Behind the gateway that is the *gateway's* address, identical for everyone — a brake inside identity-service would have thrown all people into one bucket, so the first person to mistype their password locks out the world. The usual escape, `X-Forwarded-For`, is worse than the problem here: any caller sets that header themselves, so trusting it hands the attacker the key to the counter. It is therefore not read, and a test pins that a forged one changes nothing.

It sits **outside authentication** in the strongest sense available: no token has been verified and no password hashed when it runs. And it sits **after** the health probes — a braked liveness probe would take the container out of the load balancer, making the brake itself the outage.

**The counter runs in-process**, so the gateway is pinned to one replica. The way out is a registration change, not a rewrite: `RedisDistributedRateLimitStore` satisfies the same interface.

**Noelia's own rate limiter works, and the brake stays anyway — for a smaller reason than before.** Under 3.0.1 there were *three* limiters: two did not brake, the third had no caller, and all three trusted `X-Forwarded-For` unconditionally with no trusted-proxy list anywhere. All of that is fixed. Measured against 4.0.2, same probes the brake is held to:

```
limit 3/min, per origin
  real origin 10.0.0.1                      200 200 200 429 429
  same, with a forged X-Forwarded-For       200 200 200 429 429
  X-Forwarded-For: 127.0.0.1 (the trap)     200 200 200 429 429
  real loopback origin (exempt list)        200 200 200 200 200
  same, exempt list emptied                 200 200 429 429 429
```

The third line is the one that mattered: the header that used to lift the brake without any configuration now changes nothing. `ClientAddress.Of` reads only `Connection.RemoteIpAddress`; a forwarded header works solely behind a named trust list (`TrustForwardedHeadersFrom`). `WhitelistedIps` still defaults to loopback, but that is no longer reachable by forging — only by genuinely coming from there.

**`Bremse.cs` is gone, and the reason it stood is worth keeping.** It survived three rounds of justification, each one measured and each one wrong in a different way: first "Noelia's limiters don't brake" (fixed in 4.0.0), then "its rejection is not a problem document" (fixed in 4.1.0), then "it is a global brake and ours is selective". The last one was mine and it was simply false — a limit of `0` writes no counter, so Noelia brakes exactly the paths you name. Always could.

What actually kept it was the price: `Noelia.Infrastructure` drags **44** transitive packages, and this gateway only routes. `Noelia.Http` (4.2.0) carries **none**, and the reason evaporated with it.

The lesson is worth more than the code: **a hand-written replacement outlives its reason.** Each time the stated reason was fixed, a new one was found rather than the code deleted — and every one of them held up until somebody measured it.

**In the fourteen services the module stays out for a reason that has nothing to do with Noelia and will not change:** a service behind the gateway sees the gateway as the origin — every caller as one. The brake belongs at the entrance, and there it is.

## The route map is a test, not a checklist

[`docs/routenkarte.yml`](docs/routenkarte.yml) records, for every endpoint reachable through the gateway, what it answers in **four** principals: no token, a person with no company (`tenant_id` null), someone acting for a company as `member`, and the same as `admin`. The second one is the one people forget — on a transfer market a person without a company is the *normal* case, and the rows where it differs from the two on the right are exactly where ADR-0017 is doing work.

**The fourth column is what made PBI-2 checkable at all.** Before it the map could not express "admin, not member", so nothing measured it — and nothing was enforced. Five rows show the difference; the three identity routes cannot, because the company id in their path does not exist and *both* company principals get 403 there, for two different reasons. `CompanyJourneyTests` covers those three against a company that exists, and `The_fourth_column_distinguishes_admin_from_member` pins the five by name: opening one of them is then a deliberate edit, not a silent one.

Two things drive it, and they answer different questions:

- **`RouteMapTests`** (in the gateway suite, no stack needed) asserts the map is **complete**: every route in `ocelot.json` has at least one entry. Add a route without an entry and it goes red. Without this, the map would be correct exactly until the next endpoint. The map's keys stay German (`pfad`, `ohne`, `mitglied`, `firma`, …) and the suite binds them by `[YamlMember(Alias = …)]` — rename a key and its column falls silently to 0, over a green build.
- **`scripts/route-map.sh`** (`make route-map`, needs `make up`) drives every answer against the running stack — four columns since PBI-2, and the fourth principal is built the way a person is: registered, invited with `role: "member"`, joined. The script refuses to measure unless that account really carries the company's tenant *and* the role `member` — without that check a failed join would quietly measure a person without a company, and all eight protected rows would still look green, because 403 is the right answer there for a person too. It is a script rather than a test suite on purpose: a suite that needs a stack skips itself without one, and a skipped test looks exactly like a passing one.

The reason it is a test at all: `scripts/k8s-up.sh` claimed `GET /jobs` answers 200 at a time when it answered **401**, and the script had never run, so nobody found out. (It answers 200 again today, but for a reason that was decided rather than assumed — see the map's own note.) A list nobody drives is wrong the day after it is written.

Building the map found three things a checklist would have blessed: five endpoints answered **500** on a malformed body (one of them `/auth/login`, where a missing password reached the password hasher); `/companies/withdrawal` was routed publicly and answered 401, confirming an internal door exists; and `GET /notifications` answers **405**, which reveals a path the service deliberately hides behind a 404. The first two are fixed. The third is written down in the map, not fixed.

### The gateway

Ocelot 25. `ocelot.json` carries its own reasoning in `//` comments — measured: .NET's JSON configuration provider skips them.

**The gateway serves the API and nothing else.** The UI is the Vite dev server on `:5173` in compose; the browser loads it from there and calls the gateway on `:8090` for every request. Two origins, and CORS names both (`CORS_ORIGINS`). Confirmation mails point at `WORKERTRANSFER_WEB_URL`, which is the UI — not the gateway.

It was the other way round until 11.09.2026: a `Navigation` middleware rewrote any request carrying `Sec-Fetch-Dest: document` onto a `/__ui/{alles}` route that proxied to the `web` container, so `…:8090/jobs` answered with the page while `fetch` got the resource. That is gone, deliberately (ADR-0040): one address meaning two things is a rule nobody remembers at the next endpoint, and it made the dev server a proxy target, which broke HMR through the gateway.

**The cost was paid rather than accepted, on 21.09.2026: `web` got a door of its own.** The chart used to expose only the gateway, so `make k8s-up` brought up a cluster with no reachable UI. Now the `web` Service is a `NodePort` (`web.nodePort` → host `5173`, the same port compose uses) and the origins are split — `webUrl` for the browser, `publicUrl` for the API.

Two things had to come with it, and the second is the one that would have been missed:

- **`CORS_ORIGINS` is set in the chart.** It never was, and it never mattered while the UI and the gateway shared an origin. A separate door makes every call cross-origin, and without the key Noelia falls back to `localhost:3000` in Development: a preflight that answers 204 with no `Access-Control-Allow-*` header at all.
- **`Proof 4` in `scripts/k8s-up.sh` has two halves.** That the UI answers proves the door; that the **gateway** answers a `Sec-Fetch-Dest: document` request *without* HTML proves ADR-0040 has not quietly come back. Only the second half could catch a regression, and nothing else in that script would notice it.

Route order in `ocelot.json` is pinned by `RouteOrderTests` against a *reversed* fixture. A probe that merely set all priorities equal stayed green because file order happened to be right; only reversing the order exposed it, and eight routes fell.
