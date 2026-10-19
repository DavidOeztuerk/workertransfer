import Box from "@mui/material/Box";
import { Outlet } from "react-router-dom";

import { SiteFooter } from "./SiteFooter";
import { SiteHeader } from "./SiteHeader";
import { SessionGate } from "./SessionGate";

/**
 * The frame around every page: header, content, footer.
 *
 * `SessionGate` lies around it and not inside — it loads the session ONCE at
 * start. Were the check inside every page, it would run anew at every switch,
 * and the header would flicker between signed in and signed out.
 */
export function AppLayout() {
  return (
    <SessionGate>
      <Box sx={{ minHeight: "100dvh", display: "flex", flexDirection: "column" }}>
        <SiteHeader />
        <Box component="main" sx={{ flexGrow: 1 }}>
          <Outlet />
        </Box>
        <SiteFooter />
      </Box>
    </SessionGate>
  );
}
