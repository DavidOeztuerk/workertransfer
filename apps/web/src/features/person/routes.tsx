import type { RouteObject } from "react-router-dom";

import { ConsentsPage } from "./pages/ConsentsPage";
import { ProfilePage } from "./pages/ProfilePage";

/**
 * Die Routen der persönlichen Bereiche.
 *
 * NOCH NICHT migriert: `/resume`, `/portfolio`, `/github`, `/my-data`,
 * `/settings`, `/delete-account`. Sie stehen in
 * `docs/uebergabe/frontend-person.md` als offene Punkte, in der dort
 * festgehaltenen Reihenfolge.
 */
export const personRoutes: RouteObject[] = [
  { path: "/profile", element: <ProfilePage /> },
  { path: "/consents", element: <ConsentsPage /> },
];
