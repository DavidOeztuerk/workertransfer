import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";

import { BERUFSFELDER, istBerufsfeld } from "../../lib/berufsfelder";
import type { BerufsfeldWahl } from "../../lib/berufsfelder";

/**
 * Die Auswahl des Berufsfelds (ADR-0039). Nie ein Pflichtfeld — der leere
 * Eintrag ist die Vorauswahl.
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
      label={label ?? t("occupation.label")}
      helperText={helperText ?? t("occupation.hint")}
      value={wert ?? ""}
      disabled={disabled}
      onChange={(event) => {
        const gewaehlt = event.target.value;
        // Leer ist ein gültiger Wert und heisst „keins", nicht „unverändert".
        onChange(istBerufsfeld(gewaehlt) ? gewaehlt : null);
      }}
      slotProps={{ select: { displayEmpty: true } }}
    >
      <MenuItem value="">{t("occupation.none")}</MenuItem>
      {BERUFSFELDER.map((feld) => (
        <MenuItem key={feld} value={feld}>
          {t(`occupation.field.${feld}`)}
        </MenuItem>
      ))}
    </TextField>
  );
}
