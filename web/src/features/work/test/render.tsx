import { ThemeProvider } from "@mui/material/styles";
import { configureStore } from "@reduxjs/toolkit";
import { type RenderOptions, render } from "@testing-library/react";
import type { ReactElement, ReactNode } from "react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";

import preferences from "../../../core/store/preferencesSlice";
import { buildTheme } from "../../../styles/theme";
import auth from "../../auth/store/authSlice";
import type { Membership, Session, SessionStatus } from "../../auth/types/session";

/**
 * Die Hülle, die eine Seite dieses Bereichs zum Leben braucht: Store, Theme,
 * Router.
 *
 * Sie steht hier und nicht in `src/shared/test/`, wo sie hingehörte — dieser
 * Durchgang darf `shared/` nicht anfassen. Wer die Bereiche später
 * zusammenzieht, legt sie dorthin und wirft diese Datei und ihr Gegenstück in
 * `features/auth/test/` weg.
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

export function testStore(vorgabe: AuthVorgabe = {}) {
  return configureStore({
    reducer: { auth, preferences },
    preloadedState: {
      auth: {
        // Voreinstellung „anonymous" und nicht „unknown": `unknown` heisst „noch
        // nicht gefragt", und Seiten, die darauf warten, rendern dann nichts.
        status: vorgabe.status ?? "anonymous",
        session: vorgabe.session ?? null,
        memberships: vorgabe.memberships ?? [],
        error: null,
        pending: false,
      },
    },
  });
}

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
        <ThemeProvider theme={buildTheme("light")}>
          <MemoryRouter initialEntries={[route]}>{children}</MemoryRouter>
        </ThemeProvider>
      </Provider>
    );
  }

  return { store, ...render(ui, { wrapper: Wrapper, ...options }) };
}
