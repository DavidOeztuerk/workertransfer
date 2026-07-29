import {
  Link,
  Outlet,
  RouterProvider,
  createRootRoute,
  createRoute,
  createRouter,
  useRouter,
} from "@tanstack/react-router";

import { HomeRoute } from "./routes/home";
import { LoginRoute } from "./routes/login";

function RootLayout() {
  const router = useRouter();
  const current = router.state.location.pathname;
  return (
    <>
      <nav aria-label="Hauptnavigation">
        <Link to="/">Start</Link>
        {current !== "/login" ? <Link to="/login">Anmelden</Link> : null}
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

export function App() {
  return <RouterProvider router={router} />;
}
