import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";

import { HomePage } from "./pages/HomePage";

/**
 * Die Routen dieses Bereichs — angemeldet HIER, nicht in `core/router`.
 *
 * <strong>`/` und `/overview` sind zwei Adressen und nicht eine.</strong>
 * Vorher bediente `/` beides: abgemeldet die Werbung, angemeldet die Übersicht.
 * Das hielt die Werbung zwar aus dem Weg, gab der Übersicht aber keine eigene
 * Adresse — sie war nicht verlinkbar, und ein Screenshot von `/` zeigte je nach
 * Sitzung etwas anderes. Die Weiterleitung macht `HomePage` selbst.
 *
 * Die Pfade sind englisch, ausnahmslos; die Oberfläche darüber ist deutsch.
 * Das ist kein Widerspruch, sondern die Trennung von Adresse und Text.
 */
export const publicRoutes: RouteObject[] = [
  { index: true, element: <HomePage /> },
  { path: "/overview", element: lazyRoute(() => import("./pages/OverviewPage"), "OverviewPage") },

  // Die vier rechtlichen Seiten. Sie liegen in EINEM Modul und werden deshalb
  // auch als eines geladen — vier getrennte Bündel für vier Textseiten wären
  // vier Anfragen für zusammen wenige Kilobyte.
  { path: "/imprint", element: lazyRoute(() => import("./pages/LegalPages"), "ImprintPage") },
  { path: "/privacy", element: lazyRoute(() => import("./pages/LegalPages"), "PrivacyPage") },
  { path: "/terms", element: lazyRoute(() => import("./pages/LegalPages"), "TermsPage") },
  {
    path: "/accessibility",
    element: lazyRoute(() => import("./pages/LegalPages"), "AccessibilityPage"),
  },
];
