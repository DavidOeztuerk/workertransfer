import { useEffect } from "react";
import CssBaseline from "@mui/material/CssBaseline";
import { ThemeProvider } from "@mui/material/styles";
import { I18nextProvider } from "react-i18next";
import { RouterProvider } from "react-router-dom";

import { i18n, spracheAnwenden } from "./core/i18n/i18n";
import { router } from "./core/router/Router";
import { useAppDispatch, useAppSelector } from "./core/store/hooks";
import {
  SPRACHEN,
  hatEigeneSprachwahl,
  languageSet,
  type Sprache,
} from "./core/store/preferencesSlice";
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
  const dispatch = useAppDispatch();
  const languagePreference = useAppSelector((state) => state.preferences.language);
  const accountLanguage = useAppSelector(
    (state) => state.auth.session?.language ?? null,
  );

  // Die Sprache des KONTOS gilt auf einem Gerät, das noch nie eine gewählt hat.
  //
  // Nur dann: `hatEigeneSprachwahl()` unterscheidet „nichts gespeichert" von
  // „‚Wie mein Gerät‘ gewählt", und die beiden dürfen nicht dasselbe bedeuten.
  // Sonst zöge die Kontosprache jedem, der ausdrücklich dem Gerät folgen will,
  // seine Wahl unter den Füssen weg — bei jedem Anmelden aufs Neue.
  useEffect(() => {
    if (accountLanguage === null || hatEigeneSprachwahl()) return;
    if (SPRACHEN.includes(accountLanguage as Sprache)) {
      dispatch(languageSet(accountLanguage as Sprache));
    }
  }, [dispatch, accountLanguage]);

  // Beim Zeichnen und nicht in einem Effekt: ein Effekt liefe NACH dem ersten
  // Bild, und dann steht die Seite einen Wimpernschlag lang auf Deutsch, bevor
  // sie auf Französisch umspringt. Der Aufruf ist idempotent — er tut nichts,
  // wenn die Sprache schon stimmt.
  spracheAnwenden(languagePreference);

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
