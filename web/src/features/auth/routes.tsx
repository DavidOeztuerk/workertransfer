import type { RouteObject } from "react-router-dom";

import { InvitationPage } from "./pages/InvitationPage";
import { LoginPage } from "./pages/LoginPage";
import { RegisterPage } from "./pages/RegisterPage";
import { LogoutPage } from "./pages/LogoutPage";
import { VerifyPage } from "./pages/VerifyPage";

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
  { path: "/login", element: <LoginPage /> },
  { path: "/register", element: <RegisterPage /> },
  { path: "/verify", element: <VerifyPage /> },
  { path: "/logout", element: <LogoutPage /> },
  { path: "/invitation", element: <InvitationPage /> },
];
