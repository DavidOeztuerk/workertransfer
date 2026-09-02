import type { RouteObject } from "react-router-dom";

import { ConsentsPage } from "./pages/ConsentsPage";
import { DeleteAccountPage } from "./pages/DeleteAccountPage";
import { GitHubPage } from "./pages/GitHubPage";
import { MyDataPage } from "./pages/MyDataPage";
import { PortfolioItemPage } from "./pages/PortfolioItemPage";
import { PortfolioPage } from "./pages/PortfolioPage";
import { ProfilePage } from "./pages/ProfilePage";
import { ResumePage } from "./pages/ResumePage";
import { SettingsPage } from "./pages/SettingsPage";

/**
 * Die Routen der persönlichen Bereiche.
 *
 * <strong>Alle Seiten dieses Bereichs sind migriert.</strong>
 */
export const personRoutes: RouteObject[] = [
  { path: "/profile", element: <ProfilePage /> },
  { path: "/resume", element: <ResumePage /> },
  { path: "/portfolio", element: <PortfolioPage /> },
  { path: "/portfolio/new", element: <PortfolioItemPage /> },
  { path: "/portfolio/:stelle", element: <PortfolioItemPage /> },
  { path: "/github", element: <GitHubPage /> },
  { path: "/my-data", element: <MyDataPage /> },
  { path: "/consents", element: <ConsentsPage /> },
  { path: "/settings", element: <SettingsPage /> },
  { path: "/delete-account", element: <DeleteAccountPage /> },
];
