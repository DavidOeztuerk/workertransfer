import { createBrowserRouter, type RouteObject } from "react-router-dom";

import { AppLayout } from "../../shared/components/layout/AppLayout";
import { NotFoundPage } from "../../shared/pages/NotFoundPage";
import { authRoutes } from "../../features/auth/routes";
import { personRoutes } from "../../features/person/routes";
import { workRoutes } from "../../features/work/routes";
import { companyRoutes } from "../../features/company/routes";
import { publicRoutes } from "../../features/public/routes";

/**
 * The route list.
 *
 * <strong>Every feature brings its own routes</strong> and registers them in a
 * file `features/<name>/routes.tsx`. This file only puts them together. The
 * reason is about division of labour, not aesthetics: a single route file that
 * everybody writes into is the place where two parallel changes reliably
 * collide.
 *
 * All paths are English — without exception, as in the rest of the system. The
 * UI itself is German; that is no contradiction but the separation of address
 * and text.
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
