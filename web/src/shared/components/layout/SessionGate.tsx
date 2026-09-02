import { useEffect } from "react";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { loadSession } from "../../../features/auth/store/authThunks";

/**
 * Fragt einmal, wer gerade handelt.
 *
 * <strong>Es blockiert die Seite nicht.</strong> Ein Ladebildschirm über der
 * ganzen Anwendung, nur um zu erfahren, ob jemand angemeldet ist, macht jeden
 * Kaltstart langsam — auch für die Marketingseite, die niemanden kennen muss.
 * Wer den Unterschied braucht, liest `status`, und der kennt `unknown` als
 * eigenen Wert.
 */
export function SessionGate({ children }: { children: React.ReactNode }) {
  const dispatch = useAppDispatch();
  const status = useAppSelector((state) => state.auth.status);

  useEffect(() => {
    if (status === "unknown") void dispatch(loadSession());
  }, [dispatch, status]);

  return <>{children}</>;
}
