import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";


/**
 * The routes of what a person does.
 *
 * `/careers/<slug>` lives in `features/company`: it shows a company profile with
 * its jobs and belongs there by subject.
 *
 * `/jobs/:jobId/apply` stood here and has fallen. In the end it was a SWITCH:
 * for a signed-in person it created a draft and redirected to
 * `/applications/drafts/:id`. Everything below it — the form with the cover
 * letter and the two release boxes — was unreachable since that switch, because
 * the redirect branch stood in front of it. Dead code with consent switches is
 * what later gets revived wrongly, and an address without a view of its own is
 * a stopover nobody is meant to see. Both callers — job list and career page —
 * now call `lib/startDrafts` and land directly at the cover letter.
 */
export const workRoutes: RouteObject[] = [
  { path: "/jobs", element: lazyRoute(() => import("./pages/JobsPage"), "JobsPage") },
  { path: "/applications", element: lazyRoute(() => import("./pages/ApplicationsPage"), "ApplicationsPage") },
  { path: "/applications/drafts", element: lazyRoute(() => import("./pages/DraftsPage"), "DraftsPage") },
  { path: "/applications/drafts/:id", element: lazyRoute(() => import("./pages/DraftPage"), "DraftPage") },
  { path: "/market", element: lazyRoute(() => import("./pages/MarketPage"), "MarketPage") },
  { path: "/transfers", element: lazyRoute(() => import("./pages/TransfersPage"), "TransfersPage") },
  { path: "/advisor", element: lazyRoute(() => import("./pages/AdvisorPage"), "AdvisorPage") },
  { path: "/assessments", element: lazyRoute(() => import("./pages/AssessmentsPage"), "AssessmentsPage") },
  { path: "/salaries", element: lazyRoute(() => import("./pages/SalariesPage"), "SalariesPage") },
];
