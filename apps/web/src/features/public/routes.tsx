import type { RouteObject } from "react-router-dom";

import { HomePage } from "./pages/HomePage";
import { OverviewPage } from "./pages/OverviewPage";

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
  { path: "/overview", element: <OverviewPage /> },
];
