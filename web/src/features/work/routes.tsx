import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";


/**
 * Die Routen dessen, was eine Person tut.
 *
 * `/careers/<slug>` liegt in `features/company`: sie zeigt ein
 * Unternehmensprofil samt seiner Stellen und gehoert sachlich dorthin.
 *
 * `/jobs/:jobId/apply` stand hier und ist gefallen. Sie war zuletzt eine
 * WEICHE: fuer eine angemeldete Person legte sie einen Entwurf an und leitete
 * nach `/applications/drafts/:id` weiter. Alles darunter — das Formular mit
 * Anschreiben und den zwei Freigabekaestchen — war seit dieser Weiche
 * unerreichbar, weil der Umleitungszweig davorstand. Toter Code mit
 * Einwilligungsschaltern ist das, was spaeter falsch wiederbelebt wird, und
 * eine Adresse ohne eigene Ansicht ist ein Zwischenhalt, den niemand sehen
 * soll. Beide Aufrufer — Stellenliste und Karriereseite — rufen jetzt
 * `lib/entwuerfe` und landen direkt beim Anschreiben.
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
];
