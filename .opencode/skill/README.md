# Skills in this directory are aspirational

These files describe how things **should** be built once the relevant phase
arrives. They are not a description of the repository as it stands. Several
prescribe tools that are not installed:

- `frontend.md` calls for TypeScript 7, Tailwind, Shadcn, Radix, Redux Toolkit,
  Zustand, React Hook Form, Zod, Storybook, Playwright, ESLint and Prettier.
  `apps/web` uses none of them — see [`docs/frontend.md`](../../docs/frontend.md)
  for what actually exists.
- `devops.md` describes Docker, Kubernetes, Helm and GitOps. The repo has a
  single `docker-compose.yml` for a local Postgres and no deployment story.
- `ai-agents.md` builds on `worker-ai`, which is **excluded from the uv
  workspace** because its ML dependencies have no Python-3.14 wheels.
- `worker-cli.md` documents a fluent `PlatformBuilder`. That API was rejected by
  [ADR-0003](../../docs/adr/0003-composition-root-not-fluent-builder.md) and does
  not exist; services use an explicit Composition-Root.

Where a skill and an ADR disagree, **the ADR wins** — it records a decision that
was actually taken, with its consequences.

For the current state, read [`docs/ROADMAP.md`](../../docs/ROADMAP.md).
Domain skills that match reality live in [`docs/skills/`](../../docs/skills/).

`database.md` was deleted on 2026-07-31: it was a shorter near-duplicate of
`database-layer.md`.
