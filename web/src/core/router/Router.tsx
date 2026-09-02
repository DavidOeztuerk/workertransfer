import { createBrowserRouter, type RouteObject } from "react-router-dom";

import { AppLayout } from "../../shared/components/layout/AppLayout";
import { NotFoundPage } from "../../shared/pages/NotFoundPage";
import { authRoutes } from "../../features/auth/routes";
import { personRoutes } from "../../features/person/routes";
import { workRoutes } from "../../features/work/routes";
import { companyRoutes } from "../../features/company/routes";
import { publicRoutes } from "../../features/public/routes";

/**
 * Die Routenliste.
 *
 * <strong>Jedes Feature bringt seine eigenen Routen mit</strong> und meldet sie
 * in einer Datei `features/<name>/routes.tsx` an. Diese Datei setzt sie nur
 * zusammen. Der Grund ist arbeitsteilig und nicht ästhetisch: eine einzige
 * Routendatei, in die alle schreiben, ist die Stelle, an der zwei parallele
 * Änderungen zuverlässig kollidieren.
 *
 * Alle Pfade sind englisch — ausnahmslos, wie im übrigen System. Die
 * Oberfläche selbst ist deutsch; das ist kein Widerspruch, sondern die Trennung
 * von Adresse und Text.
 */
const routes: RouteObject[] = [
  {
    path: "/",
    element: <AppLayout />,
    errorElement: <NotFoundPage />,
    children: [
      ...publicRoutes,
      ...authRoutes,
      ...personRoutes,
      ...workRoutes,
      ...companyRoutes,
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];

export const router = createBrowserRouter(routes);
