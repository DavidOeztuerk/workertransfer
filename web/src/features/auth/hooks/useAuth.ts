import { useAppDispatch } from "../../../core/store/hooks";
import { useActor, type Actor } from "../../work/lib/session";
import { loadSession, login, logout, refreshSession } from "../store/authThunks";

/**
 * A thin facade over Redux — login, logout, refresh, session.
 *
 * No roles from the token (ADR-0018: membership per operation). No JWT decode,
 * no storage. Tokens stay httpOnly cookies.
 */
export function useAuth(): Actor & {
  login: (email: string, password: string) => void;
  logout: () => void;
  refresh: () => void;
  load: () => void;
} {
  const actor = useActor();
  const dispatch = useAppDispatch();

  return {
    ...actor,
    login: (email, password) => {
      void dispatch(login({ email, password }));
    },
    logout: () => {
      void dispatch(logout());
    },
    refresh: () => {
      void dispatch(refreshSession());
    },
    load: () => {
      void dispatch(loadSession());
    },
  };
}
