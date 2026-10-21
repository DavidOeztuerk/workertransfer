import FormControlLabel from "@mui/material/FormControlLabel";
import FormHelperText from "@mui/material/FormHelperText";
import Box from "@mui/material/Box";
import Switch from "@mui/material/Switch";

/**
 * The switch for a release — the ONE place that renders a consent toggle.
 *
 * <strong>A switch and not a checkbox</strong> — and that is no matter of
 * taste: a checkbox promises that the change applies only on submit. For a
 * consent it takes effect AT ONCE (ADR-0013), and whoever draws a box here
 * makes a promise the application breaks.
 *
 * MUI's `Switch` is an `input[type=checkbox]` with `role="switch"` — the role
 * is what counts: screen readers say "switch, on/off" instead of "checkbox,
 * checked".
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
      {/* Under the label, not under the switch: MUI's switch is 58 px wide and
          the label pulls 11 px back, so its text begins at 47 px. A hint flush
          with the switch read as a separate paragraph, and in a list of nine
          the eye lost which hint belongs to which line. */}
      {hint ? <FormHelperText sx={{ ml: "47px", mt: 0 }}>{hint}</FormHelperText> : null}
    </Box>
  );
}
