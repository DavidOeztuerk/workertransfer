import type { RouteObject } from "react-router-dom";

import { ApplicationsPage } from "./pages/ApplicationsPage";
import { JobsPage } from "./pages/JobsPage";

/**
 * Die Routen dessen, was eine Person tut.
 *
 * NOCH NICHT migriert: `/careers/<slug>`, `/jobs/:id/apply`, `/market`, `/transfers`. Sie stehen in
 * `docs/uebergabe/frontend-work-company.md` als offene Punkte — samt der
 * Reihenfolge und der Hinweise, welche alte Quelldatei jeweils die Vorlage ist
 * (Achtung: `routes/career.tsx`, nicht `careers.tsx`).
 */
export const workRoutes: RouteObject[] = [
  { path: "/jobs", element: <JobsPage /> },
  { path: "/applications", element: <ApplicationsPage /> },
];
