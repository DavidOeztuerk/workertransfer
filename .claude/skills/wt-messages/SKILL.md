---
name: wt-messages
description: Use when working on WorkerTransfer's messages-service (ADR-0047) — conversations per process (`application`, `approach`, `initiative`), who belongs to a conversation, drafts that only a human releases, the reply context sent to a model, `origin`, the absence of read state, the AI advisor for the person (ADR-0049), `message_received`, or what erasure does to messages.
---

# messages-service

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there.

- `docs/adr/0047-nachrichten-pro-vorgang-und-freigegebene-entwuerfe.md` — messages per process, and drafts only a human being releases. 07.10.2026.

### messages-service: one conversation per process, and a draft goes out only released

Built 07.10.2026 under [ADR-0047](docs/adr/0047-nachrichten-pro-vorgang-und-freigegebene-entwuerfe.md) — Phase 1 of `docs/BAULISTE.md`, the ground every later agent stands on (VISION-FRAGEN D2, E1, E2). Phase 1 knows one kind of process: **`application`**.

**Who belongs to a conversation is decided by the process's own service, not here.** Before every read and write, `HttpProcessLookup` asks `GET /applications/{id}` **with the caller's token**, synchronously, no cache; 404 there is 404 here, byte-identical whether or not the conversation exists. The capacity in the token decides the side: acting for the company → `company`, acting as oneself → `person`. A silent applications-service is **503**, never 404.

**A draft is a message in state `draft`, and only its own side sees it** — filtered in the SQL (`VisibleToAsync`), not further up. It comes into being on a click (`POST …/draft`), a human being edits and **releases** it; discarding deletes the row. The model call is a *query* (`ComposeReplyQuery`) and the draft is saved by a separate command, so no transaction stays open for the seconds a model takes. There is **no route** that sends a draft without a release; `MessageJourneyTests` pins the closed set of six.

**What reaches the model:** `ReplyContext` — the writing side, the last twenty *sent* messages of this one conversation, the person's name (only to be masked) and a language. Never a draft, a profile, a CV or another conversation; the rules forbid judging the other side (D2/D3). The person's side uses their own AI access if they set one up, the company's side the company's own key if an admin stored one, otherwise the platform's model; a silent identity-service is refused, not quietly replaced by the platform's provider (ADR-0051).

**`origin` (`human`/`agent`) is shown to the own side only**, and no author id ever goes out; whether a recipient must learn that a model drafted a released message is lawyer question J2.

**No read state, no unread count** — "has the company read it?" is pressure and a statement about a person's behaviour. The ninth notification kind, `message_received`, names nobody; a person writing notifies every member but the writer.

**The approach** (second kind, `approach`, Phase 2): a company's first message to a person who released their profile to it, opened only by `POST /messages/approaches` with at least a `recruiter` (ADR-0048) and a yes from the ledger. One conversation per company/person pair — the reference is derived from both, never the subject id itself (it stands in URLs). Its parties stand in the conversation; the **company side asks the ledger at every request**, the person side never — a revocation stops the company and leaves the person reading. The first text is the scout's approach draft (the AI recruiter), released in the hit card; `/messages` lists every conversation with its counterpart only (`company_id` for the person, `person_id` for the company).

**The unsolicited application** (third kind, `initiative`, 08.10.2026, VISION-FRAGEN B2) is the approach's mirror: a person writes to a company from its careers page, and **sending releases her profile to that company** — written with *her* token through `IReleaseWriter`, the one place messages-service writes to the ledger. Text is checked before the release, the company's members before either (none → 404, no release).

**The AI advisor** ([ADR-0049](docs/adr/0049-der-ki-berater-ist-ein-agent-der-person.md), 08.10.2026, VISION-FRAGEN B4/B5/D4): `POST /messages/{kind}/{ref}/advice` — on a click, for the person only (a company gets 403; an agent for both sides is the conflict D4 forbids). It reads the sent messages of that one conversation and **her own facts with her token** — mandate (advisor-service) and earliest start (transfer-service, H2) — and nothing else (`AdviceContext` is pinned). A silent source is written into the prompt as *not reachable*, never left out. No legal verdict (§ 2 RDG), no number about her, every statement with its source; the advice is stored nowhere and sent to nobody. Speaking *for* her goes through the existing draft-and-release path.

**Erasure:** the person's conversations fall whole, the company's messages in them too. An erased *member*'s released messages stay in the other person's conversation with `author_id = null`; their unreleased drafts go.
