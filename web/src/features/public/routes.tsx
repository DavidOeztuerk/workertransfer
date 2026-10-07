import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";

import { HomePage } from "./pages/HomePage";

/**
 * The routes of this area — registered HERE, not in `core/router`.
 *
 * <strong>`/` and `/overview` are two addresses and not one.</strong> Before,
 * `/` served both: the advertising signed out, the overview signed in. That
 * kept the advertising out of the way but gave the overview no address of its
 * own — it was not linkable, and a screenshot of `/` showed something different
 * depending on the session. `HomePage` does the redirect itself.
 *
 * The paths are English, without exception; the UI above them is German. That
 * is no contradiction but the separation of address and text.
 */
export const publicRoutes: RouteObject[] = [
  { index: true, element: <HomePage /> },
  { path: "/overview", element: lazyRoute(() => import("./pages/OverviewPage"), "OverviewPage") },
  { path: "/checklists", element: lazyRoute(() => import("./pages/ChecklistsPage"), "ChecklistsPage") },

  // The four legal pages. They lie in ONE module and are therefore loaded as
  // one — four separate bundles for four text pages would be four requests for
  // a few kilobytes together.
  { path: "/imprint", element: lazyRoute(() => import("./pages/LegalPages"), "ImprintPage") },
  { path: "/privacy", element: lazyRoute(() => import("./pages/LegalPages"), "PrivacyPage") },
  { path: "/terms", element: lazyRoute(() => import("./pages/LegalPages"), "TermsPage") },
  {
    path: "/accessibility",
    element: lazyRoute(() => import("./pages/LegalPages"), "AccessibilityPage"),
  },
];
