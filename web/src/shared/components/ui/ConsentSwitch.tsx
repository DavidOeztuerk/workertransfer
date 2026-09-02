import FormControlLabel from "@mui/material/FormControlLabel";
import FormHelperText from "@mui/material/FormHelperText";
import Box from "@mui/material/Box";
import Switch from "@mui/material/Switch";

/**
 * Der Schalter für eine Freigabe.
 *
 * <strong>Ein Schalter und keine Ankreuzbox</strong> — und das ist keine
 * Geschmacksfrage: eine Ankreuzbox verspricht, dass die Änderung erst beim
 * Absenden gilt. Bei einer Einwilligung wirkt sie SOFORT (ADR-0013), und wer
 * hier eine Box zeichnet, macht ein Versprechen, das die Anwendung bricht.
 *
 * MUIs `Switch` ist ein `input[type=checkbox]` mit `role="switch"` — die Rolle
 * ist das, was zählt: Screenreader sagen "Schalter, ein/aus" statt
 * "Kontrollkästchen, aktiviert".
 */
export function ConsentSwitch({
  label,
  hint,
  checked,
  disabled,
  onChange,
}: {
  label: string;
  hint?: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (next: boolean) => void;
}) {
  return (
    <Box sx={{ display: "flex", flexDirection: "column", gap: 0.5 }}>
      <FormControlLabel
        control={
          <Switch
            checked={checked}
            disabled={disabled}
            onChange={(event) => onChange(event.target.checked)}
            slotProps={{ input: { role: "switch" } }}
          />
        }
        label={label}
      />
      {hint ? <FormHelperText sx={{ ml: 0 }}>{hint}</FormHelperText> : null}
    </Box>
  );
}
