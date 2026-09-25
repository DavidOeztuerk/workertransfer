import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";

import { OCCUPATIONAL_FIELDS, isOccupationalField } from "../../lib/occupationalFields";
import type { OccupationalFieldChoice } from "../../lib/occupationalFields";

/**
 * The choice of the occupational field (ADR-0039). Never a required field —
 * the empty entry is the preselection.
 */
export function OccupationalFieldSelect({
  value,
  onChange,
  disabled = false,
  label,
  helperText,
}: {
  value: OccupationalFieldChoice;
  onChange: (field: OccupationalFieldChoice) => void;
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
      value={value ?? ""}
      disabled={disabled}
      onChange={(event) => {
        const chosen = event.target.value;
        // Empty is a valid value and means "none", not "unchanged".
        onChange(isOccupationalField(chosen) ? chosen : null);
      }}
      slotProps={{ select: { displayEmpty: true } }}
    >
      <MenuItem value="">{t("occupation.none")}</MenuItem>
      {OCCUPATIONAL_FIELDS.map((field) => (
        <MenuItem key={field} value={field}>
          {t(`occupation.field.${field}`)}
        </MenuItem>
      ))}
    </TextField>
  );
}
