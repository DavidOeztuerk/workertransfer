import CssBaseline from "@mui/material/CssBaseline";
import { ThemeProvider } from "@mui/material/styles";
import { RouterProvider } from "react-router-dom";

import { router } from "./core/router/Router";
import { useThemeMode } from "./shared/hooks/useThemeMode";

/**
 * Die Anwendung unterhalb des Stores.
 *
 * Der `ThemeProvider` steht HIER und nicht in `main.tsx`, weil er das Theme aus
 * `useThemeMode` bekommt — und das liest die Vorliebe aus dem Store. Ein Hook
 * braucht eine Komponente unterhalb des `Provider`; in `main.tsx` gäbe es die
 * noch nicht.
 */
export function AppRoot() {
  const { theme } = useThemeMode();

  return (
    <ThemeProvider theme={theme}>
      {/* Setzt auch `color-scheme`, damit Scrollbalken und Formularfelder des
          Browsers im Dunkelmodus mitziehen — sonst bleiben sie hell. */}
      <CssBaseline />
      <RouterProvider router={router} />
    </ThemeProvider>
  );
}
