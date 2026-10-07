import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";

/** The conversations of the caller, person or company (ADR-0047). */
export const messageRoutes: RouteObject[] = [
  { path: "/messages", element: lazyRoute(() => import("./pages/MessagesPage"), "MessagesPage") },
];
