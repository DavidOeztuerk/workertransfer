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
 * stehenbleiben: die Kandidatenliste liegt auf `GET /candidates` und nicht
 * `/profiles` (ein absichtlich toter Praefix), und `/company/jobs` und
 * `/company/jobs/new` sind zwei Seiten — eine E2E-Reise sucht das Formular
 * heute noch auf der Liste.
 */
export const companyRoutes: RouteObject[] = [
  { path: "/careers/:slug", element: lazyRoute(() => import("./pages/CareerPage"), "CareerPage") },
  { path: "/candidates", element: lazyRoute(() => import("./pages/CandidatesPage"), "CandidatesPage") },
  { path: "/company/jobs", element: lazyRoute(() => import("./pages/CompanyJobsPage"), "CompanyJobsPage") },
  { path: "/company/jobs/new", element: lazyRoute(() => import("./pages/CompanyJobNewPage"), "CompanyJobNewPage") },
  { path: "/company/team", element: lazyRoute(() => import("./pages/CompanyTeamPage"), "CompanyTeamPage") },
  { path: "/company/team/invite", element: lazyRoute(() => import("./pages/CompanyTeamInvitePage"), "CompanyTeamInvitePage") },
  { path: "/company/profile", element: lazyRoute(() => import("./pages/CompanyProfilePage"), "CompanyProfilePage") },
  { path: "/company/transfers", element: lazyRoute(() => import("./pages/CompanyTransfersPage"), "CompanyTransfersPage") },
];
