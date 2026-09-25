import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";


/**
 * The routes of the personal areas.
 *
 * <strong>All pages of this area are migrated.</strong>
 */
export const personRoutes: RouteObject[] = [
  { path: "/profile", element: lazyRoute(() => import("./pages/ProfilePage"), "ProfilePage") },
  { path: "/resume", element: lazyRoute(() => import("./pages/ResumePage"), "ResumePage") },
  { path: "/portfolio", element: lazyRoute(() => import("./pages/PortfolioPage"), "PortfolioPage") },
  { path: "/portfolio/new", element: lazyRoute(() => import("./pages/PortfolioItemPage"), "PortfolioItemPage") },
  { path: "/portfolio/:index", element: lazyRoute(() => import("./pages/PortfolioItemPage"), "PortfolioItemPage") },
  { path: "/github", element: lazyRoute(() => import("./pages/GitHubPage"), "GitHubPage") },
  { path: "/my-data", element: lazyRoute(() => import("./pages/MyDataPage"), "MyDataPage") },
  { path: "/consents", element: lazyRoute(() => import("./pages/ConsentsPage"), "ConsentsPage") },
  { path: "/settings", element: lazyRoute(() => import("./pages/SettingsPage"), "SettingsPage") },
  { path: "/delete-account", element: lazyRoute(() => import("./pages/DeleteAccountPage"), "DeleteAccountPage") },
];
