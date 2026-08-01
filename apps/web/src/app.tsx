import {
  Link,
  Outlet,
  createRootRoute,
  createRoute,
  createRouter,
  useRouter,
} from "@tanstack/react-router";

import { useCompanies, useSwitchCompany } from "./auth/companies";
import { useLogout, useSession } from "./auth/session";
import { CompanyNewRoute } from "./routes/company-new";
import { HomeRoute } from "./routes/home";
import { LoginRoute } from "./routes/login";
import { RegisterRoute } from "./routes/register";
import { VerifyRoute } from "./routes/verify";

export function RootLayout() {
  const router = useRouter();
  const current = router.state.location.pathname;
  const { user, isLoading } = useSession();
  const logout = useLogout();

  return (
    <>
      <nav aria-label="Hauptnavigation">
        <Link to="/">Start</Link>
        {isLoading ? null : user !== null ? (
          <>
            <span>Angemeldet</span>
            <CompanySwitcher activeTenantId={user.tenant_id} />
            <Link to="/company/new">Unternehmen anlegen</Link>
            <button type="button" onClick={() => logout.mutate()} disabled={logout.isPending}>
              {logout.isPending ? "Abmeldung läuft…" : "Abmelden"}
            </button>
          </>
        ) : current !== "/login" ? (
          <>
            <Link to="/login">Anmelden</Link>
            <Link to="/register">Registrieren</Link>
          </>
        ) : null}
      </nav>
      <Outlet />
    </>
  );
}

function CompanySwitcher({ activeTenantId }: { activeTenantId: string | null }) {
  const { companies } = useCompanies();
  const switchTo = useSwitchCompany();
  if (companies.length === 0) return null;

  const active = companies.find((c) => c.id === activeTenantId);
  return (
    <label>
      Handeln als
      <select
        value={activeTenantId ?? ""}
        onChange={(e) => switchTo.mutate(e.target.value)}
        disabled={switchTo.isPending}
      >
        {/* "als Person" ist der Standardzustand — ein Unternehmen wird bewusst
            gewählt (ADR-0017). Zurückwechseln heißt neu anmelden, weil der
            Tenant im Token steckt und es keinen Endpunkt gibt, der ihn wieder
            entfernt. Solange eines aktiv ist, wäre die Option also eine Lüge:
            sie würde POST /auth/company/ auslösen und wortlos 404 liefern. */}
        <option value="" disabled={activeTenantId !== null}>
          {activeTenantId === null ? "Ich selbst" : "Ich selbst (Abmelden nötig)"}
        </option>
        {companies.map((company) => (
          <option key={company.id} value={company.id}>
            {company.name}
          </option>
        ))}
      </select>
      {active !== undefined ? <span>{active.role}</span> : null}
    </label>
  );
}

function CompanyNewWithSession() {
  // The route needs the principal; the component takes it as a prop so it stays
  // testable without a live session.
  const { user } = useSession();
  return <CompanyNewRoute principal={user} />;
}

const rootRoute = createRootRoute({ component: RootLayout });
const homeRoute = createRoute({ getParentRoute: () => rootRoute, path: "/", component: HomeRoute });
const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/login",
  component: LoginRoute,
});

const registerRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/register",
  component: RegisterRoute,
});
const verifyRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/verify",
  component: VerifyRoute,
});
const companyNewRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/company/new",
  component: CompanyNewWithSession,
});

const routeTree = rootRoute.addChildren([
  homeRoute,
  loginRoute,
  registerRoute,
  verifyRoute,
  companyNewRoute,
]);

export const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
