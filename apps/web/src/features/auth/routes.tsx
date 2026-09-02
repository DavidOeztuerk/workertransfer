import type { RouteObject } from "react-router-dom";

import { LoginPage } from "./pages/LoginPage";
import { RegisterPage } from "./pages/RegisterPage";
import { VerifyPage } from "./pages/VerifyPage";

/**
 * Die Routen des Anmeldebereichs.
 *
 * `/login` und `/register` sind zwei eigene Adressen und kein Umschalter auf
 * einer: beide sind teilbar, der Zurück-Knopf tut was er soll, und der Wechsel
 * dazwischen steht als Reiter AUF der Seite — nicht in der Kopfzeile, wo er
 * vorher je nach Seite verschwand.
 *
 * NOCH NICHT migriert: `/invitation` und `/logout`. Sie stehen in
 * `docs/uebergabe/frontend-public-auth.md` als offene Punkte.
 */
export const authRoutes: RouteObject[] = [
  { path: "/login", element: <LoginPage /> },
  { path: "/register", element: <RegisterPage /> },
  { path: "/verify", element: <VerifyPage /> },
];
