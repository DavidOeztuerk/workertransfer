import type { RouteObject } from "react-router-dom";

import { lazyRoute } from "../../shared/components/routing/lazyRoute";


/**
 * Die Routen des Anmeldebereichs.
 *
 * `/login` und `/register` sind zwei eigene Adressen und kein Umschalter auf
 * einer: beide sind teilbar, der Zurück-Knopf tut was er soll, und der Wechsel
 * dazwischen steht als Reiter AUF der Seite — nicht in der Kopfzeile, wo er
 * vorher je nach Seite verschwand.
 *
 * `/invitation` loest einen Einladungslink ein — genau einmal, denn ein
 * Token ist einmalig und ein zweiter Versuch zeigte einen Fehler fuer etwas,
 * das gerade gelungen ist.
 */
export const authRoutes: RouteObject[] = [
  { path: "/login", element: lazyRoute(() => import("./pages/LoginPage"), "LoginPage") },
  { path: "/register", element: lazyRoute(() => import("./pages/RegisterPage"), "RegisterPage") },
  { path: "/verify", element: lazyRoute(() => import("./pages/VerifyPage"), "VerifyPage") },
  { path: "/logout", element: lazyRoute(() => import("./pages/LogoutPage"), "LogoutPage") },
  { path: "/invitation", element: lazyRoute(() => import("./pages/InvitationPage"), "InvitationPage") },
];
