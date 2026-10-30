---
name: wt-naming
description: Use before renaming or translating anything in WorkerTransfer — a type, member, file, route, settings property, configuration or environment key, JSON name, column or table — and when translating the still-German identifiers in `web/`. Holds the code-in-English rule, the contracts a rename must not move (wire, database, settings = environment key), where German deliberately stays, and points to `docs/NAMING.md`.
---

# Naming and renaming

Moved verbatim from `CLAUDE.md`, „Conventions that bite" (30.10.2026). The
binding short form — code is English, the wire and the database do not move, a
settings property name is the environment key — stays there.

- **Code is written in English. All of it, backend and frontend.** Type names, members, locals, parameters, test method names, comments and XML docs. This is not a style preference and it is not negotiable: it was asked for explicitly, and it drifted back to German twice because it was nowhere written down.
  - **The backend is translated** — `src/`, `tests/`, `scripts/`, the Makefile, compose, the chart and the configuration keys, 21.–25.09.2026 (`10e0157` … `a4ec721`; each commit body lists its renames). **`web/` is not yet**: its identifiers, file names (`kontext.ts`, `fehler.ts`) and comments are still German. Where German deliberately stayed in the backend — the prompts sent to a model, the check ids (`wt.loeschung.nachweis`), the exemption reasons and finding texts that land in the German evidence documents, the keys of `docs/routenkarte.yml` — the line says why.
  - **The wire and the database were always English** and stay exactly as they are: 170 `[JsonPropertyName]` values, every `HasColumnName`, every `ToTable`. A rename touches identifiers only — no migration, no contract change. If a rename would move a JSON name or a column, the rename is wrong.
  - **German stays in exactly two places**: the text a person reads (`web/src/core/i18n/kataloge/de.ts` — that is the product's language, and `en`/`fr` are translations of it), and prose documents like this one, the ADRs and `docs/`.
  - Domain words with no faithful English equivalent (`Ablage`, `Personenzeile`) are renamed to what they *mean* (`DocumentStore`, `SubjectKeyedTable`), never transliterated. Where the German term is load-bearing in a document, the comment says so.
  - **A settings property name IS the environment key, and it is the third contract beside the wire and the database.** `ConsentSettings.BaseUrl` binds `Consent__BaseUrl`; renaming the property alone leaves the value on its default, silently — measured 22.09.2026: `Storage__Wurzel` → `Root` made `POST /portfolios/me/attachments` answer 500 over a green build, and five of twenty portfolio journeys fell (the key is `Storage__Root` today). Some of those keys sit in the shared compose anchor (`Erasure__Secret`), so **one service cannot move alone**: the keys are renamed in a single pass over code, `docker-compose.yml`, the chart, `.env.example` and the scripts, or not at all. That pass happened on 25.09.2026 (`229de78`), with no German alias left beside a key. `ComposeKeyTests` reads `docker-compose.yml` and requires every key a service's block sets to land on a settings property of that service — and the old German keys to land nowhere; it is the only suite that reads what compose sets, which is why both earlier breaks got past every other one.
  - **`docs/NAMING.md` holds the stem table and the six places where an identifier is also a contract.** Read it before a rename; the build finds none of those six.
