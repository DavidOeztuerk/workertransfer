import type { RouteObject } from "react-router-dom";

/**
 * Die Routen dessen, was ein Unternehmen tut.
 *
 * <strong>Noch leer.</strong> Keine Seite dieses Bereichs ist migriert:
 * `/candidates`, `/company/jobs`, `/company/jobs/new`, `/company/team`
 * (+ `/invite`), `/company/profile`, `/company/transfers`.
 *
 * Die Reihenfolge und die Vorlagen stehen in
 * `docs/uebergabe/frontend-work-company.md`. Zwei Dinge daraus, die man beim
 * Bauen sonst falsch macht: die Kandidatenliste liegt auf `GET /candidates` und
 * nicht `/profiles` (ein absichtlich toter Präfix), und die alte Vorlage für
 * `/company/team` heisst `routes/team.tsx`, nicht `company-team.tsx`.
 */
export const companyRoutes: RouteObject[] = [];
