import { useEffect } from "react";
import CssBaseline from "@mui/material/CssBaseline";
import { ThemeProvider } from "@mui/material/styles";
import { I18nextProvider } from "react-i18next";
import { RouterProvider } from "react-router-dom";

import { i18n, applyLanguage } from "./core/i18n/i18n";
import { router } from "./core/router/Router";
import { useAppDispatch, useAppSelector } from "./core/store/hooks";
import {
  LANGUAGES,
  hasOwnLanguageChoice,
  languageSet,
  type Language,
} from "./core/store/preferencesSlice";
import { useThemeMode } from "./shared/hooks/useThemeMode";

/**
 * The application below the store.
 *
 * The `ThemeProvider` stands HERE and not in `main.tsx` because it gets the
 * theme from `useThemeMode` — and that reads the preference from the store. A
 * hook needs a component below the `Provider`; in `main.tsx` there is none
 * yet.
 *
 * `CssBaseline` also sets `color-scheme`, so the browser's scrollbars and form
 * fields follow dark mode instead of staying light.
 */
export function AppRoot() {
  const { theme } = useThemeMode();
  const dispatch = useAppDispatch();
  const languagePreference = useAppSelector((state) => state.preferences.language);
  const accountLanguage = useAppSelector(
    (state) => state.auth.session?.language ?? null,
  );

  // The ACCOUNT's language applies on a device that has never chosen one.
  //
  // Only then: `hasOwnLanguageChoice()` tells "nothing stored" from "chose
  // 'like my device'", and the two must not mean the same. Otherwise the
  // account language would pull the choice from under everybody who explicitly
  // wants to follow their device — anew at every sign-in.
  useEffect(() => {
    if (accountLanguage === null || hasOwnLanguageChoice()) return;
    if (LANGUAGES.includes(accountLanguage as Language)) {
      dispatch(languageSet(accountLanguage as Language));
    }
  }, [dispatch, accountLanguage]);

  // While drawing and not in an effect: an effect would run AFTER the first
  // frame, and then the page stands in German for the blink of an eye before
  // it jumps to French. The call is idempotent — it does nothing when the
  // language is already right.
  applyLanguage(languagePreference);

  return (
    <I18nextProvider i18n={i18n}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <RouterProvider router={router} />
      </ThemeProvider>
    </I18nextProvider>
  );
}
