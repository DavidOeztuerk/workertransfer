import type { RouteObject } from "react-router-dom";

import { CandidatesPage } from "./pages/CandidatesPage";
import { CareerPage } from "./pages/CareerPage";

/**
 * Die Routen dessen, was ein Unternehmen tut.
 *
 * <strong>Fast leer.</strong> Nur `/careers/<slug>` steht; es fehlen noch:
 * `/company/jobs`, `/company/jobs/new`, `/company/team`
 * (+ `/invite`), `/company/profile`, `/company/transfers`.
 *
 * Die Reihenfolge und die Vorlagen stehen in
 * `docs/uebergabe/frontend-work-company.md`. Zwei Dinge daraus, die man beim
 * Bauen sonst falsch macht: die Kandidatenliste liegt auf `GET /candidates` und
 * nicht `/profiles` (ein absichtlich toter Präfix), und die alte Vorlage für
 * `/company/team` heisst `routes/team.tsx`, nicht `company-team.tsx`.
 */
export const companyRoutes: RouteObject[] = [
  { path: "/careers/:slug", element: <CareerPage /> },
  { path: "/candidates", element: <CandidatesPage /> },
];
