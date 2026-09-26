import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";


/**
 * The routes of the sign-in area.
 *
 * `/login` and `/register` are two addresses of their own and not a toggle on
 * one: both can be shared, the back button does what it should, and the switch
 * between them stands as a tab ON the page — not in the header, where it used
 * to vanish depending on the page.
 *
 * `/invitation` redeems an invitation link — exactly once, because a token is
 * single-use and a second attempt showed an error for something that had just
 * succeeded.
 */
export const authRoutes: RouteObject[] = [
  { path: "/login", element: lazyRoute(() => import("./pages/LoginPage"), "LoginPage") },
  { path: "/register", element: lazyRoute(() => import("./pages/RegisterPage"), "RegisterPage") },
  { path: "/verify", element: lazyRoute(() => import("./pages/VerifyPage"), "VerifyPage") },
  { path: "/logout", element: lazyRoute(() => import("./pages/LogoutPage"), "LogoutPage") },
  { path: "/invitation", element: lazyRoute(() => import("./pages/InvitationPage"), "InvitationPage") },
];
