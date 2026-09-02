import type { RouteObject } from "react-router-dom";

import { ApplicationsPage } from "./pages/ApplicationsPage";
import { JobApplyPage } from "./pages/JobApplyPage";
import { JobsPage } from "./pages/JobsPage";
import { MarketPage } from "./pages/MarketPage";
import { TransfersPage } from "./pages/TransfersPage";

/**
 * Die Routen dessen, was eine Person tut.
 *
 * `/careers/<slug>` liegt in `features/company`: sie zeigt ein
 * Unternehmensprofil samt seiner Stellen und gehoert sachlich dorthin.
 */
export const workRoutes: RouteObject[] = [
  { path: "/jobs", element: <JobsPage /> },
  { path: "/applications", element: <ApplicationsPage /> },
  { path: "/market", element: <MarketPage /> },
  { path: "/transfers", element: <TransfersPage /> },
  { path: "/jobs/:jobId/apply", element: <JobApplyPage /> },
];
