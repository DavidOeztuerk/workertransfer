---
name: wt-scout
description: Use when working on WorkerTransfer's scout-service or anything that searches for people — `/scout`, the OR search, the internal profile search door, evidence fetched to a hit, stored searches, `profile_discovered`, the scout per advert (`/scout/jobs/{id}/candidates`), the radius tick (ADR-0041), filters that were decided against, or the approach draft. Also for `docs/SCOUT-UND-BERATER.md`, which is a draft, and for the `application_received` mail to a company's members.
---

# scout-service

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there;
read `wt-person-rules` (ADR-0022) alongside.

### scout-service: ticks and evidence, never a number

Built 11.09.2026 under ADR-0036. It replaced `GET /candidates` in
profile-service, and **`/candidates` is gone** — endpoint, gateway route, client
and the UI, all in the same step. `docs/routenkarte.yml` keeps the old path as a
**dead door** (404 in all four columns) and `RoutingTests` measures that no
route stands behind it any more. Two searches side by side would have been two
truths.

Three things fell with it, because they had no caller left: the AND branch in
the profile store, the batch question in profile-service's consent gate, and the
per-card GitHub fetch in the browser. A branch without a caller is what later
gets revived wrongly.

The hard parts of `/candidates` were **carried over, not reinvented**: the
company requirement (a person acting for themselves gets 403), the ledger asked
**per row over `/check-batch`** (ADR-0030, synchronous, no cache), **no total
count** (ADR-0026 — the difference to the page length would say how many
profiles are *not* released), no filling up a short page, and the stable order
`updated_at DESC, id DESC`.

**Four conditions, each a test** (`ConditionsTests`), and each with a counter-probe
that was measured to fall:

1. **No ordering by fit.** Two identical searches give the same order, and
   whoever matches more of the searched words does *not* move up. There is
   nothing to sort by either: a rank out of a tick list needs an invented number
   first.
2. **No number about a person.** A twin of `Adr0022Tests` over Domain and
   Contracts, forbidding `fit`, `score`, `rank`, `percent`, `passung`, `anzahl`
   and their siblings — German and English side by side since the translation,
   so `count`, `total` and `rating` stand there too — which is why the page size
   in this service is called `PageLength`, never `Count`.
3. **Only what was *named* is searchable.** Evidence (GitHub topics, language
   names) is **fetched to the hit, never used to find it**: a topic is a
   statement about an *artefact*, and searching by it would quietly make it one
   about the person (ADR-0033). `SearchFilter` has no field for one, and evidence
   is fetched only *after* the ledger has answered — so nothing is looked up
   about someone who released nothing.
4. **The approach is a draft.** The service has no postman, no address and no
   send path; `ConditionsTests` scans the application assembly for one. The text
   goes to the browser of whoever asked and lies in a form.

**The search is an OR, and that is what makes the tick list an answer.**
`/candidates` searched with AND — every hit then satisfies every condition,
every tick would be set, and "which skill is missing" would have no answer;
condition 1 would be empty too, since there would be nothing to sort. The
internal door in profile-service (`GET /internal/profiles/search`, behind the
shared secret, no gateway route) therefore searches with OR, and it is the only
mode left.

**That door deliberately does not ask the ledger.** The check stands one line
higher, in scout-service, for the whole page at once: exactly *one* place decides
about visibility, and two would be two truths. Whoever uses that door for
something else fetches the check along with it.

**Stored is the request, never the result** (`searches`): filters and a name. A
stored result about people goes stale against a withdrawal, and a withdrawal has
to take effect on the next call (ADR-0013). For the same reason there is no
nightly run that searches and mails "new hits" to the company — that would be the
same cache with an alarm clock in front of it.

**"Your profile was discovered"** is its own notification kind
(`profile_discovered`, ADR-0033). It names **no company**, travels over the
content-free outbox (ADR-0025), and is capped at **one per person per day** —
and that cap takes the *inbox entry* with it, not just the mail. One entry per
hit would be a counter over one's own visibility, and on a detour exactly the
register of who looked at whom that this design refuses to build. The cap lives
in notification-service and is answered from the person's own inbox, so no new
row is needed anywhere; delivery stays at-least-once and a second delivery is
harmless.

There is no `/me/scouting` and no table of who looked at whom.

**The scout per advert** (07.10.2026, ADR-0036 Ergänzung, VISION-FRAGEN B1): `GET /scout/jobs/{id}/candidates` is *this* search with the company's own published advert's skills as the words — all four conditions unchanged, opened on a click from `/company/jobs`, never run in the background. A draft, closed or foreign advert is one 404; an advert without skills is 422, never the empty search (which would list everybody who released). Up to twenty skills go in; profile-service's door used to cut silently at ten.

**Three filters PBI-3 names are decided against, not pending** (ADR-0036 §6–8):
the **occupational field** never becomes a filter — it contradicts ADR-0039
("no visibility follows from it") and would find *less*, since the vocabulary
already carries MIG/WIG/CNC/SPS; **availability** is not filtered, because the
market status has its own release and filtering by it would reveal
approachability without asking; and the **radius** is built as
[ADR-0041](docs/adr/0041-entfernung-ist-eine-frage-zwischen-zwei-aussagen.md) —
a person states how far they will commute (10/25/50/100/egal), an advert states
its attendance, and the two make a **tick**, never a kilometre figure and never
a filter that removes somebody. `?stelle=<id>` on the search adds the tick and
takes no hit away. Nothing new is stored: `PlaceLookup` resolves the existing
free-text location at search time, so **no coordinate column on a person** was
created.

**That ADR asked for a field that already existed**, and the build found it:
jobs-service has carried `RemoteMode` (`none`/`hybrid`/`full`) since forever,
with the same argument in its own comment — *"hybrid is the commonest case and
not an intermediate step of true"*. No second field was added; the existing
value is read under its own words. The lesson is the older one: **search for
the purpose before deciding to build it.**

### Still not built: nothing from that draft

[`docs/SCOUT-UND-BERATER.md`](docs/SCOUT-UND-BERATER.md) is a **draft**. The inventory in [`docs/SCOUT-UND-BERATER-BESTAND.md`](docs/SCOUT-UND-BERATER-BESTAND.md) measured what already exists and **refutes parts of that draft**. ADR-0034 is the cover-letter agent, ADR-0035 the application folder.

**All three services of that draft are now built**: `scout-service` (ADR-0036), `advisor-service` (ADR-0037) and `assessment-service` (ADR-0042), 11.09.2026 — each has its own section above.

When an application arrives, company **members** get a mail of kind `application_received`. A company has no mailbox. The outbox stays content-free (ADR-0025): id and kind, never a name.
