import { Link as RouterLink } from "react-router-dom";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";

/**
 * Der Wechsel zwischen Anmelden und Registrieren — HIER, nicht in der Kopfzeile.
 *
 * Vorher standen beide Wege oben und blendeten sich gegenseitig je nach Seite
 * aus. Das ist die falsche Stelle: oben sind sie zwei konkurrierende Angebote,
 * hier sind sie <em>eine Entscheidung mit zwei Antworten</em> — und wer sich
 * vertan hat, sieht den anderen Weg genau dort, wo er gerade hinschaut.
 *
 * Echte Verweise und keine Umschalter: beide Zustände haben eine eigene
 * Adresse, sind teilbar, und der Zurück-Knopf tut, was er soll.
 */
export function AuthModeTabs({ current }: { current: "login" | "register" }) {
  return (
    <Tabs
      value={current}
      variant="fullWidth"
      sx={{ mb: 3, borderBottom: 1, borderColor: "divider" }}
      aria-label="Anmelden oder neues Konto anlegen"
    >
      <Tab component={RouterLink} to="/login" value="login" label="Anmelden" />
      <Tab
        component={RouterLink}
        to="/register"
        value="register"
        label="Konto anlegen"
      />
    </Tabs>
  );
}
