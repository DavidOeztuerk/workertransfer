---
name: wt-advisor
description: Use when working on WorkerTransfer's advisor-service — the mandate (four values), the three stages and the capabilities they write, `advance` and taking a stage back, fields absent rather than locked, excluded companies and the revoked public profile, the handover to transfer-service, identity's internal company and person doors, or `advisor_conversation`.
---

# advisor-service

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there.

### advisor-service: the mandate is a view, not a second store

Built 11.09.2026 under ADR-0037. It holds **four values and nothing else** —
entry month, salary range, workload, excluded companies. Visibility lives in
the ledger, availability in the market status, and a mandate table for either
would break ADR-0020 word for word: a withdrawal would then have to take effect
in two places, so one day it takes effect in one.

`MandateRow` and `ConversationRow` carry **no column whose name contains**
`sichtbar`, `visible`, `visibility`, `public`, `freigabe`, `release`, `stage` or
`stufe`, and `ConditionsTests` reads the **EF model** to say so. The last two words are the
sharper half: a stage column would be visibility as a second door beside the
ledger.

**Three stages, and each one is a release by the person.** They are the
*existing* capabilities, not three new ones — a second capability for "profile
visible to company X" would be a second truth (ADR-0037 decision 2):

| stage | stands on | `advance` writes |
|---|---|---|
| 1 | `profile.visibility:tenant:<id>` **or** `:public` | `profile.visibility:tenant:<id>`, `market.visibility:tenant:<id>` |
| 2 | `resume.visibility:tenant:<id>` | that plus `documents.visibility:tenant:<id>` |
| 3 | `advisor.identity:tenant:<uuid>` | the same |

The two columns come apart on purpose: a stage promises more than one row.
Stage 1 promises *availability*, and availability **is** the market status —
a stage that says three things and silently delivers two is the "locked" hint
in reverse. `github.visibility:public` is deliberately **not** written: it is
platform-wide and has no per-company form, so granting it would turn a release
to *one* company into a release to all.

**The new capability carries no digit**, and that is the reason not to number:
`Capability`'s namespace admits none, so `advisor.stage1` would be refused.
A capability says *what* becomes visible, never *how far* somebody has come.

**Cumulative, both ways.** Releasing stage 3 also writes 1 and 2 — otherwise
the new capability stands while the one below it is missing, and a read still
says 0. Taking back stage 1 takes 2 and 3 with it: the CV visible and the
profile not would be a permission without a foundation.

**What is not released does not exist for the other side.** Not `null`, not
"locked", not a grey line: the field is *absent* from the JSON
(`JsonIgnoreCondition.WhenWritingNull` on `ConversationV1`). And it is absent in
exactly the same way when the stage *is* released and the person never filled
the field in — hidden and not present stay indistinguishable (ADR-0020 §1).
A conversation at stage 0 falls out of the company's list altogether; an empty
row would be the same hint in list form. `POST /conversations` therefore
answers **404** in three different situations — no release, stage 0, company
excluded — byte-identical apart from the correlation id.

**The acceptance — "the current employer does not see their own staff in the
scout" — is kept in the ledger and nowhere else.** Not a second gate in the
scout, which would be a second place deciding about visibility. The ledger
knows **no denial**: "visible to everyone except X" cannot be expressed in it,
so ADR-0037 says the mode "everyone" stops existing the moment somebody names
an X. `WriteMandateHandler` therefore revokes `profile.visibility:public`
in the same step — with the person's own token, *before* saving, so a silent
ledger leaves the mandate as it was rather than producing the half-here state.

**The stage is never cached, and the handover is never rebuilt.** Every read
asks the ledger (ADR-0013). Agreement belongs to the person (`agree`); the
handover is the company's step (`hand-over`) and calls transfer-service's
existing `POST /transfers` with the company's token — the triangle consensus
(market release, approachable, employer release) is checked *there*. A refusal
from there is 409, a silence 503: "cannot" and "we do not know" are two things.

**Two internal doors at identity-service came with it**, both behind the shared
secret and without a gateway route: `GET /internal/companies/{tenantId}` hands
out name and proven domain (an exclusion is named by domain, and a second
domain table here would be the one that goes stale first), and
`GET /internal/account/{subjectId}/identity` hands out civil name and email —
**two fields, no postal address, no phone** (ADR-0038). The secret there proves
that a *service* is asking, not that it *may*: the permission stands one line
higher, in the ledger, and it is fetched **before** this call. Whoever uses
that door for something else fetches the check along with it.

`"advisor"` is in `ErasureRecipient.Services` from its first table, so the
cascade is twelve rows now, not eleven — and thirteen since assessment-service
(ADR-0042).

**One notification kind came with it**, `advisor_conversation` — the seventh,
and the counterpart to `profile_discovered`. There somebody *searched*, which
is why it names no company ("the person can do nothing with it as long as
nobody has approached them"); here somebody *has* approached, and with a
company the person had already released something to. It names no company
either: a notification carries only a kind.
