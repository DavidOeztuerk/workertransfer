import { ThemeProvider } from "@mui/material/styles";
import { configureStore } from "@reduxjs/toolkit";
import { type RenderOptions, render } from "@testing-library/react";
import type { ReactElement, ReactNode } from "react";
import { I18nextProvider } from "react-i18next";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";

import preferences from "../../../core/store/preferencesSlice";
import { buildTheme } from "../../../styles/theme";
import { i18n } from "../../../core/i18n/i18n";
import auth from "../store/authSlice";
import type { Membership, Session, SessionStatus } from "../types/session";

/**
 * The shell a page of this area needs to come alive: store, theme, router.
 *
 * It stands here and not in `src/shared/test/`, where it would belong — this
 * pass was not allowed to touch `shared/`. It is the literal counterpart of
 * `features/public/test/render.tsx`; whoever later merges the areas puts one
 * of them into `shared/test/` and throws both away here.
 *
 * <strong>A store of its own per call.</strong> The real store is a singleton;
 * across test boundaries it would carry the previous test's state along, and a
 * result would then depend on the order of the tests.
 */
export interface AuthPreset {
  status?: SessionStatus;
  session?: Session | null;
  memberships?: Membership[];
}

export function testStore(preset: AuthPreset = {}) {
  return configureStore({
    reducer: { auth, preferences },
    preloadedState: {
      auth: {
        // Default "anonymous" and not "unknown": `unknown` means "not asked
        // yet", and pages waiting for it then render nothing.
        status: preset.status ?? "anonymous",
        session: preset.session ?? null,
        memberships: preset.memberships ?? [],
        error: null,
        pending: false,
      },
    },
  });
}

/**
 * The catalogues belong in the test host: without `I18nextProvider`
 * `useTranslation` returns the key instead of the text, and a test looking for
 * `language.label` then says nothing about the UI. German is pinned, like in the
 * Playwright configuration and for the same reason (ADR-0031).
 */
export function renderWithStore(
  ui: ReactElement,
  {
    auth: preset,
    route = "/",
    ...options
  }: RenderOptions & { auth?: AuthPreset; route?: string } = {}
) {
  const store = testStore(preset);

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <Provider store={store}>
        <I18nextProvider i18n={i18n}>
          <ThemeProvider theme={buildTheme("light")}>
            <MemoryRouter initialEntries={[route]}>{children}</MemoryRouter>
          </ThemeProvider>
        </I18nextProvider>
      </Provider>
    );
  }

  return { store, ...render(ui, { wrapper: Wrapper, ...options }) };
}
