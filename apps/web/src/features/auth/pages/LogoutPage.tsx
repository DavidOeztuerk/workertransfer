import { useEffect, useRef } from "react";
import { Navigate } from "react-router-dom";

import { LoadingBlock } from "../../../shared/components/ui";
import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { logout } from "../store/authThunks";

/**
 * Abmelden — eine eigene Adresse und kein Knopf im Kopf.
 *
 * <strong>Warum eine Seite.</strong> Das Abmelden räumt ein Cookie ab, das der
 * Browser nie zu sehen bekommt; nur der Server kann es. Als Adresse ist der
 * Vorgang teilbar, aus einem Menü heraus erreichbar und — wichtiger — er hat
 * einen sichtbaren Zwischenzustand. Ein Knopf, der still nichts tut, weil die
 * Anfrage hängt, sieht aus wie ein kaputtes Menü.
 *
 * <strong>Genau einmal.</strong> React 19 ruft Effekte im StrictMode doppelt
 * auf; ohne die Sperre liefe der Abmeldebefehl zweimal, und der zweite träfe
 * eine Sitzung, die es nicht mehr gibt. Das wäre folgenlos, aber es stünde als
 * Fehler im Protokoll — und ein Protokoll, in dem harmlose Fehler stehen, wird
 * nicht mehr gelesen.
 */
export function LogoutPage() {
  const dispatch = useAppDispatch();
  const status = useAppSelector((state) => state.auth.status);
  const gestartet = useRef(false);

  useEffect(() => {
    if (gestartet.current) return;
    gestartet.current = true;
    void dispatch(logout());
  }, [dispatch]);

  // Erst wenn der Server bestätigt hat. Vorher weiterzuleiten hiesse, eine
  // Abmeldung zu behaupten, die vielleicht nicht stattgefunden hat.
  if (status === "anonymous") {
    return <Navigate to="/" replace />;
  }

  return <LoadingBlock label="Du wirst abgemeldet …" />;
}
