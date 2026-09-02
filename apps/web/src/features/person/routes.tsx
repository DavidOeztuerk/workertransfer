import type { RouteObject } from "react-router-dom";

import { ConsentsPage } from "./pages/ConsentsPage";
import { DeleteAccountPage } from "./pages/DeleteAccountPage";
import { ProfilePage } from "./pages/ProfilePage";
import { SettingsPage } from "./pages/SettingsPage";

/**
 * Die Routen der persönlichen Bereiche.
 *
 * NOCH NICHT migriert: `/resume`, `/portfolio`, `/github`, `/my-data`. Sie stehen in
 * `docs/uebergabe/frontend-person.md` als offene Punkte, in der dort
 * festgehaltenen Reihenfolge.
 */
export const personRoutes: RouteObject[] = [
  { path: "/profile", element: <ProfilePage /> },
  { path: "/consents", element: <ConsentsPage /> },
  { path: "/settings", element: <SettingsPage /> },
  { path: "/delete-account", element: <DeleteAccountPage /> },
];
