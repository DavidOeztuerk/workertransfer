import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";


/**
 * Die Routen dessen, was ein Unternehmen tut.
 *
 * <strong>Alle Seiten dieses Bereichs sind migriert.</strong>
 *
 * `/careers/<slug>` liegt hier und nicht in `work`: sie zeigt ein
 * Unternehmensprofil samt seiner Stellen und gehoert sachlich hierher.
 *
 * Zwei Dinge, die man beim Bauen sonst falsch macht und die deshalb
 * stehenbleiben: die Suche nach Menschen liegt auf `GET /scout/candidates` bei
 * scout-service und nicht mehr auf `/candidates` bei profile-service (dieser
 * Pfad ist am 11.09.2026 gefallen, ADR-0036), und `/company/jobs` und
 * `/company/jobs/new` sind zwei Seiten — eine E2E-Reise sucht das Formular
 * heute noch auf der Liste.
 */
export const companyRoutes: RouteObject[] = [
  { path: "/careers/:slug", element: lazyRoute(() => import("./pages/CareerPage"), "CareerPage") },
  { path: "/scout", element: lazyRoute(() => import("./pages/ScoutPage"), "ScoutPage") },
  { path: "/company/jobs", element: lazyRoute(() => import("./pages/CompanyJobsPage"), "CompanyJobsPage") },
  { path: "/company/jobs/new", element: lazyRoute(() => import("./pages/CompanyJobNewPage"), "CompanyJobNewPage") },
  { path: "/company/team", element: lazyRoute(() => import("./pages/CompanyTeamPage"), "CompanyTeamPage") },
  { path: "/company/team/invite", element: lazyRoute(() => import("./pages/CompanyTeamInvitePage"), "CompanyTeamInvitePage") },
  { path: "/company/profile", element: lazyRoute(() => import("./pages/CompanyProfilePage"), "CompanyProfilePage") },
  { path: "/company/transfers", element: lazyRoute(() => import("./pages/CompanyTransfersPage"), "CompanyTransfersPage") },
  { path: "/company/advisor", element: lazyRoute(() => import("./pages/CompanyAdvisorPage"), "CompanyAdvisorPage") },
  { path: "/company/assessments", element: lazyRoute(() => import("./pages/CompanyAssessmentsPage"), "CompanyAssessmentsPage") },
  { path: "/company/applications", element: lazyRoute(() => import("./pages/CompanyApplicationsPage"), "CompanyApplicationsPage") },
  { path: "/company/applications/:id", element: lazyRoute(() => import("./pages/CompanyApplicationPage"), "CompanyApplicationPage") },
];
