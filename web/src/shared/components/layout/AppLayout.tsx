import Box from "@mui/material/Box";
import { Outlet } from "react-router-dom";

import { SiteHeader } from "./SiteHeader";
import { SessionGate } from "./SessionGate";

/**
 * Der Rahmen um jede Seite: Kopfzeile, Inhalt.
 *
 * `SessionGate` liegt darum und nicht darin — es lädt die Sitzung EINMAL beim
 * Start. Läge die Prüfung in jeder Seite, liefe sie bei jedem Wechsel neu, und
 * die Kopfzeile flackerte zwischen angemeldet und abgemeldet.
 */
export function AppLayout() {
  return (
    <SessionGate>
      <Box sx={{ minHeight: "100dvh", display: "flex", flexDirection: "column" }}>
        <SiteHeader />
        <Box component="main" sx={{ flexGrow: 1 }}>
          <Outlet />
        </Box>
      </Box>
    </SessionGate>
  );
}
