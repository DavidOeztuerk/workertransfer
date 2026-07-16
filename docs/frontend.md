# Frontend architecture

The frontend is a pnpm workspace driven by Turborepo:

```text
apps/web              Vite + React application shell
packages/ui           accessible, product-owned UI primitives and design tokens
```

The application owns feature modules, pages, route composition, API clients, and
server-state hooks. `packages/ui` contains only portable design-system primitives;
it must not import feature logic, application state, or backend clients.

## Commands

```bash
pnpm install
pnpm check
pnpm test
pnpm build
pnpm dev
```

`VITE_API_BASE_URL` is declared as a Turborepo build input. It will be the only
browser-visible backend base URL; secrets never use a `VITE_` prefix.
