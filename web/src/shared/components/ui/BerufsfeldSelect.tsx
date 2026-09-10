import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";

import { BERUFSFELDER, istBerufsfeld } from "../../lib/berufsfelder";
import type { BerufsfeldWahl } from "../../lib/berufsfelder";

/**
 * Die Auswahl des Berufsfelds — an der Registrierung und in den Einstellungen
 * dieselbe (ADR-0039).
 *
 * <strong>Nie ein Pflichtfeld.</strong> Der leere Eintrag steht ganz oben und
 * ist die Vorauswahl: eine Pflichtangabe an der Anmeldung wäre eine Hürde vor
 * dem ersten Nutzen, und sie zwänge jemanden, der zwischen zwei Welten steht,
 * sich zu entscheiden, bevor er gesehen hat, wofür.
 *
 * Ein Auswahlfeld und kein Freitext, weil aus dieser Angabe eine Navigation
 * folgt und eine Navigation für jeden möglichen Wert eine Antwort haben muss.
 */
export function BerufsfeldSelect({
  wert,
  onChange,
  disabled = false,
  label,
  helperText,
}: {
  wert: BerufsfeldWahl;
  onChange: (feld: BerufsfeldWahl) => void;
  disabled?: boolean;
  label?: string;
  helperText?: string;
}) {
  const { t } = useTranslation();

  return (
    <TextField
      select
      label={label ?? t("beruf.label")}
      helperText={helperText ?? t("beruf.hinweis")}
      value={wert ?? ""}
      disabled={disabled}
      onChange={(event) => {
        const gewaehlt = event.target.value;
        // Der leere Eintrag ist ein gültiger Wert und heisst „keins" — nicht
        // „unverändert". Ohne ihn wäre eine einmal getroffene Wahl endgültig.
        onChange(istBerufsfeld(gewaehlt) ? gewaehlt : null);
      }}
      slotProps={{ select: { displayEmpty: true } }}
    >
      <MenuItem value="">{t("beruf.keins")}</MenuItem>
      {BERUFSFELDER.map((feld) => (
        <MenuItem key={feld} value={feld}>
          {t(`beruf.feld.${feld}`)}
        </MenuItem>
      ))}
    </TextField>
  );
}
