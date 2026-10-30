---
name: wt-assessment
description: Use when working on WorkerTransfer's assessment-service (work samples, ADR-0042) — the stated effort in hours (1 to 8), the deadline, the single evaluation the person always reads, why there is no decline route and no number, the byte-identical answer to both sides, text-and-one-link submissions that are never fetched, or `assessment_update`.
---

# assessment-service

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there.

### assessment-service: the effort in hours, and one evaluation the person reads

Built 11.09.2026 under ADR-0042 — **the ADR is 0042 and not 0040**, which PBI-5
asks for: 0040 (the gateway serves no UI) and 0041 (distance) were already taken.

The service exists because a work sample invites **two** abuses that are easy to
mistake for one. The first is **unpaid work**: a task without a stated size is
one whose price you learn after paying it. The second is **the mark**: a
company's judgement of a person's work is exactly what ADR-0022 removed from
this tree — the moment it leaves the process. It only has to leave once.

**The measurement that set the shape:** `Bewerbungsstand` has five values and
neither `Bewerbung` nor `BewerbungV1` (today `ApplicationState`,
`JobApplication`, `ApplicationV1`) carries **one field in which a company
writes anything back to the person**. For an application that is defensible — a
company forced to justify writes boilerplate. For a work sample it is exactly
backwards, because there the person *worked*, and an outright rejection is the
trade this service must not broker: work for silence.

Three rules, each a mechanism rather than a paragraph:

1. **The evaluation belongs to the process.** No number — no mark, no stars, no
   percentage. And no way out of the process: the store knows two questions
   ("this company's cases" and "my cases"), the closed endpoint set in
   `ConditionsTests` is six, and there is no third question a company could ask
   about a person. The alternative — a ledger capability "my evaluations are
   visible to company Y" — was rejected because the switch would exist, and then
   the pressure to flip it would: a switch you press under pressure is not a
   consent, and a reference letter with consent paint is still a reference letter.
2. **The person sees it, always.** There is exactly **one** evaluation field and
   it is the one the person reads; `GET /assessments/{id}` answers both sides
   **byte-identically**, and a test compares them byte for byte. The ledger
   guards the company side and **never** the person's — whoever revokes
   disappears for the company (404, as everywhere) and goes on reading what was
   written about their work. An evaluation you can be locked out of by
   withdrawing visibility is a judgement the judged never reads. And an outcome
   without text is **422**: rejecting and giving a reason are one step.
   Evaluated **once**; a second time is 409.
3. **The effort is stated up front, and declining is recorded nowhere.**
   `hours` is required, 1 to 8 — beyond one working day it is not a sample but
   work, and work has a contract, not a form. The deadline lies at least 48
   hours out. **There is no decline route, state or field**: whoever does not
   want to does nothing, the deadline passes, and `expired` is indistinguishable
   from "meant to and didn't". A polite decline button is kinder to the company
   and creates the record — "declined three times" is a fact about a person,
   assembled from individually reasonable clicks.

It stands on `profile.visibility:*` — **the existing capability**, the same one
stage 1 of a conversation stands on, and deliberately no `assessment.*`: a
second capability for the same question is a second truth. There is no call to
advisor-service either; both ask the ledger, and the ledger is the one place.

The submission is **text and at most one link, never bytes** — a third file
store would be a third place an erasure must reach. **The link is never
fetched**: it is stored and shown, a person clicks it. `ConditionsTests` pins that
exactly two types take an `IHttpClientFactory` (the ledger and notification).

Erasure recipient from its first table, and **no retention case, not even for
the evaluation** — it is not a record belonging to somebody else. The cascade is
thirteen rows now.

The eighth notification kind came with it: `assessment_update`, for both
movements ("a task was set for you", "your evaluation is in"). The second is why
the kind exists — without it a person learns of a reply at their next visit.
