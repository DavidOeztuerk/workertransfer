import {
  Link,
  Outlet,
  createRootRoute,
  createRoute,
  createRouter,
  useRouter,
} from "@tanstack/react-router";

import { useLogout, useSession } from "./auth/session";
import { HomeRoute } from "./routes/home";
import { LoginRoute } from "./routes/login";

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
          <Link to="/login">Anmelden</Link>
        ) : null}
      </nav>
      <Outlet />
    </>
  );
}

const rootRoute = createRootRoute({ component: RootLayout });
const homeRoute = createRoute({ getParentRoute: () => rootRoute, path: "/", component: HomeRoute });
const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/login",
  component: LoginRoute,
});

const routeTree = rootRoute.addChildren([homeRoute, loginRoute]);

export const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
