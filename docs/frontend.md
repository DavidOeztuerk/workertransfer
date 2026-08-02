# Frontend architecture

The frontend is a pnpm workspace driven by Turborepo:

```text
apps/web              Vite + React application shell
packages/ui           accessible, product-owned UI primitives and design tokens
```

The application owns feature modules, pages, route composition, API clients, and
server-state hooks. `packages/ui` contains only portable design-system primitives;
it must not import feature logic, application state, or backend clients.

## What exists today (2026-07-31)

The rule above is the target, not a description. Concretely:

- **Two routes**: `/` (static marketing page) and `/login` (works end-to-end against
  identity-service). No authenticated area, no route guards, no 404 component.
- **Session state**: `src/auth/session.ts` (`useSession` / `useLogout`) is the single
  source of "am I logged in", backed by TanStack Query over `GET /me`. The nav
  reflects it. This is the only server-state hook.
- **`packages/ui`**: `Button` and `Card`. Hand-written CSS with `--wt-*` custom
  properties on `:root`. **No** Tailwind, Shadcn, Radix, Storybook, ESLint or
  Prettier — the `.opencode/skill/frontend.md` skill prescribes all of those, but
  none is installed. The package exports raw TypeScript source (no build step).
- **No i18n layer.** German is hardcoded in JSX and in one place in the API client
  (`"Anmeldung fehlgeschlagen"`); tests assert the German literals directly.
- **Forms** use plain `useState` — no React Hook Form, no Zod.

## Commands

```bash
pnpm install
pnpm check      # tsc --noEmit across the workspace
pnpm test       # Vitest (apps/web and packages/ui)
pnpm build
pnpm dev
make check-web  # pnpm check + pnpm test, the binding gate steps 5 and 6
```

All four run in CI's `frontend-quality` job on Node 24.

`VITE_API_BASE_URL` is declared as a Turborepo build input. It will be the only
browser-visible backend base URL; secrets never use a `VITE_` prefix.
