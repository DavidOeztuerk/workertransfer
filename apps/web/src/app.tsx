import {
  Link,
  Outlet,
  createRootRoute,
  createRoute,
  createRouter,
  useRouter,
} from "@tanstack/react-router";

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
