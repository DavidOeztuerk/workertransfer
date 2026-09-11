import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";


/**
 * Die Routen dessen, was eine Person tut.
 *
 * `/careers/<slug>` liegt in `features/company`: sie zeigt ein
 * Unternehmensprofil samt seiner Stellen und gehoert sachlich dorthin.
 */
export const workRoutes: RouteObject[] = [
  { path: "/jobs", element: lazyRoute(() => import("./pages/JobsPage"), "JobsPage") },
  { path: "/applications", element: lazyRoute(() => import("./pages/ApplicationsPage"), "ApplicationsPage") },
  { path: "/applications/drafts", element: lazyRoute(() => import("./pages/DraftsPage"), "DraftsPage") },
  { path: "/applications/drafts/:id", element: lazyRoute(() => import("./pages/DraftPage"), "DraftPage") },
  { path: "/market", element: lazyRoute(() => import("./pages/MarketPage"), "MarketPage") },
  { path: "/transfers", element: lazyRoute(() => import("./pages/TransfersPage"), "TransfersPage") },
  { path: "/advisor", element: lazyRoute(() => import("./pages/AdvisorPage"), "AdvisorPage") },
  { path: "/jobs/:jobId/apply", element: lazyRoute(() => import("./pages/JobApplyPage"), "JobApplyPage") },
];
