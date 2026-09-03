import CssBaseline from "@mui/material/CssBaseline";
import { ThemeProvider } from "@mui/material/styles";
import { I18nextProvider } from "react-i18next";
import { RouterProvider } from "react-router-dom";

import { i18n, spracheAnwenden } from "./core/i18n/i18n";
import { router } from "./core/router/Router";
import { useAppSelector } from "./core/store/hooks";
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
  const sprachvorliebe = useAppSelector((state) => state.preferences.language);

  // Beim Zeichnen und nicht in einem Effekt: ein Effekt liefe NACH dem ersten
  // Bild, und dann steht die Seite einen Wimpernschlag lang auf Deutsch, bevor
  // sie auf Französisch umspringt. Der Aufruf ist idempotent — er tut nichts,
  // wenn die Sprache schon stimmt.
  spracheAnwenden(sprachvorliebe);

  return (
    <I18nextProvider i18n={i18n}>
      <ThemeProvider theme={theme}>
        {/* Setzt auch `color-scheme`, damit Scrollbalken und Formularfelder des
            Browsers im Dunkelmodus mitziehen — sonst bleiben sie hell. */}
        <CssBaseline />
        <RouterProvider router={router} />
      </ThemeProvider>
    </I18nextProvider>
  );
}
