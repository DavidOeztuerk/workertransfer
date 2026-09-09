import { useAppDispatch } from "../../../core/store/hooks";
import { useHandelnder, type Handelnder } from "../../work/lib/session";
import { erneuern, loadSession, login, logout } from "../store/authThunks";

/**
 * Dünne Fassade über Redux — Login, Logout, Refresh, Sitzung.
 *
 * Keine Rollen aus dem Token (ADR-0018: Mitgliedschaft je Vorgang). Kein
 * JWT-Decode, kein Speicher. Tokens bleiben httpOnly-Cookies.
 */
export function useAuth(): Handelnder & {
  login: (email: string, password: string) => void;
  logout: () => void;
  refresh: () => void;
  load: () => void;
} {
  const handelnder = useHandelnder();
  const dispatch = useAppDispatch();

  return {
    ...handelnder,
    login: (email, password) => {
      void dispatch(login({ email, password }));
    },
    logout: () => {
      void dispatch(logout());
    },
    refresh: () => {
      void dispatch(erneuern());
    },
    load: () => {
      void dispatch(loadSession());
    },
  };
}
