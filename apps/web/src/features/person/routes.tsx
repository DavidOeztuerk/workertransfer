import type { RouteObject } from "react-router-dom";

import { ConsentsPage } from "./pages/ConsentsPage";
import { DeleteAccountPage } from "./pages/DeleteAccountPage";
import { PortfolioItemPage } from "./pages/PortfolioItemPage";
import { PortfolioPage } from "./pages/PortfolioPage";
import { ProfilePage } from "./pages/ProfilePage";
import { ResumePage } from "./pages/ResumePage";
import { SettingsPage } from "./pages/SettingsPage";

/**
 * Die Routen der persönlichen Bereiche.
 *
 * NOCH NICHT migriert: `/github`, `/my-data`. Sie stehen in
 * `docs/uebergabe/frontend-person.md` als offene Punkte, in der dort
 * festgehaltenen Reihenfolge.
 */
export const personRoutes: RouteObject[] = [
  { path: "/profile", element: <ProfilePage /> },
  { path: "/resume", element: <ResumePage /> },
  { path: "/portfolio", element: <PortfolioPage /> },
  { path: "/portfolio/new", element: <PortfolioItemPage /> },
  { path: "/portfolio/:stelle", element: <PortfolioItemPage /> },
  { path: "/consents", element: <ConsentsPage /> },
  { path: "/settings", element: <SettingsPage /> },
  { path: "/delete-account", element: <DeleteAccountPage /> },
];
