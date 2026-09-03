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
import auth from "../../auth/store/authSlice";
import type { Membership, Session, SessionStatus } from "../../auth/types/session";

/**
 * Die Hülle, die eine Seite dieses Bereichs zum Leben braucht: Store, Theme,
 * Router.
 *
 * Sie steht hier und nicht in `src/shared/test/`, wo sie hingehörte — dieser
 * Durchgang darf `shared/` nicht anfassen. Sie ist damit die dritte Kopie
 * (`features/public/test/`, `features/auth/test/`); wer die Bereiche später
 * zusammenzieht, legt eine nach `shared/` und wirft die anderen weg.
 *
 * <strong>Ein eigener Store je Aufruf.</strong> Der echte Store ist ein
 * Einzelstück; über Testgrenzen hinweg trüge er den Zustand des vorigen Tests
 * weiter, und dann hängt ein Ergebnis an der Reihenfolge der Tests.
 */
export interface AuthVorgabe {
  status?: SessionStatus;
  session?: Session | null;
  memberships?: Membership[];
}

export const PERSON: Session = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "anna@example.com",
  tenantId: null, language: "de",
};

export function testStore(vorgabe: AuthVorgabe = {}) {
  return configureStore({
    reducer: { auth, preferences },
    preloadedState: {
      auth: {
        // Voreinstellung „anonymous" und nicht „unknown": `unknown` heisst „noch
        // nicht gefragt", und die Seiten dieses Bereichs zeichnen dann einen
        // Ladezustand statt eines Inhalts.
        status: vorgabe.status ?? "anonymous",
        session: vorgabe.session ?? null,
        memberships: vorgabe.memberships ?? [],
        error: null,
        pending: false,
      },
    },
  });
}

/** Kurzform für „angemeldet als Person, ohne Unternehmen" — der Normalfall. */
export const ANGEMELDET: AuthVorgabe = { status: "authenticated", session: PERSON };

export function renderMitStore(
  ui: ReactElement,
  {
    auth: vorgabe,
    route = "/",
    ...options
  }: RenderOptions & { auth?: AuthVorgabe; route?: string } = {}
) {
  const store = testStore(vorgabe);

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <Provider store={store}>
        {/*
          Die Kataloge gehoeren in den Testwirt, sonst zeigt `useTranslation`
          den Schluessel statt des Textes — und ein Test, der `sprache.label`
          sucht, sagt nichts ueber die Oberflaeche. Deutsch fest, wie in der
          Playwright-Konfiguration und aus demselben Grund (ADR-0031).
        */}
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
