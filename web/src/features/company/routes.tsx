import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";


/**
 * The routes of what a company does.
 *
 * <strong>All pages of this area are migrated.</strong>
 *
 * `/careers/<slug>` lies here and not in `work`: it shows a company profile
 * with its jobs and belongs here by subject.
 *
 * Two things one otherwise gets wrong when building, and which therefore stay:
 * the search for people lies on `GET /scout/candidates` at scout-service and no
 * longer on `/candidates` at profile-service (that path fell on 11.09.2026,
 * ADR-0036), and `/company/jobs` and `/company/jobs/new` are two pages — an E2E
 * journey still looks for the form on the list today.
 */
export const companyRoutes: RouteObject[] = [
  { path: "/careers/:slug", element: lazyRoute(() => import("./pages/CareerPage"), "CareerPage") },
  { path: "/scout", element: lazyRoute(() => import("./pages/ScoutPage"), "ScoutPage") },
  { path: "/company/jobs", element: lazyRoute(() => import("./pages/CompanyJobsPage"), "CompanyJobsPage") },
  { path: "/company/jobs/new", element: lazyRoute(() => import("./pages/CompanyJobNewPage"), "CompanyJobNewPage") },
  { path: "/company/jobs/:id/edit", element: lazyRoute(() => import("./pages/CompanyJobNewPage"), "CompanyJobEditPage") },
  { path: "/company/team", element: lazyRoute(() => import("./pages/CompanyTeamPage"), "CompanyTeamPage") },
  { path: "/company/team/invite", element: lazyRoute(() => import("./pages/CompanyTeamInvitePage"), "CompanyTeamInvitePage") },
  { path: "/company/profile", element: lazyRoute(() => import("./pages/CompanyProfilePage"), "CompanyProfilePage") },
  { path: "/company/transfers", element: lazyRoute(() => import("./pages/CompanyTransfersPage"), "CompanyTransfersPage") },
  { path: "/company/advisor", element: lazyRoute(() => import("./pages/CompanyAdvisorPage"), "CompanyAdvisorPage") },
  { path: "/company/assessments", element: lazyRoute(() => import("./pages/CompanyAssessmentsPage"), "CompanyAssessmentsPage") },
  { path: "/company/applications", element: lazyRoute(() => import("./pages/CompanyApplicationsPage"), "CompanyApplicationsPage") },
  { path: "/company/applications/:id", element: lazyRoute(() => import("./pages/CompanyApplicationPage"), "CompanyApplicationPage") },
];
