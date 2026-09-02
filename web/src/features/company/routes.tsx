import type { RouteObject } from "react-router-dom";

import { CandidatesPage } from "./pages/CandidatesPage";
import { CompanyJobNewPage } from "./pages/CompanyJobNewPage";
import { CompanyJobsPage } from "./pages/CompanyJobsPage";
import { CompanyTeamInvitePage } from "./pages/CompanyTeamInvitePage";
import { CompanyProfilePage } from "./pages/CompanyProfilePage";
import { CompanyTeamPage } from "./pages/CompanyTeamPage";
import { CompanyTransfersPage } from "./pages/CompanyTransfersPage";
import { CareerPage } from "./pages/CareerPage";

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
  { path: "/careers/:slug", element: <CareerPage /> },
  { path: "/candidates", element: <CandidatesPage /> },
  { path: "/company/jobs", element: <CompanyJobsPage /> },
  { path: "/company/jobs/new", element: <CompanyJobNewPage /> },
  { path: "/company/team", element: <CompanyTeamPage /> },
  { path: "/company/team/invite", element: <CompanyTeamInvitePage /> },
  { path: "/company/profile", element: <CompanyProfilePage /> },
  { path: "/company/transfers", element: <CompanyTransfersPage /> },
];
