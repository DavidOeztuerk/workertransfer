import CheckIcon from "@mui/icons-material/Check";
import Box from "@mui/material/Box";
import ListItemIcon from "@mui/material/ListItemIcon";
import MenuItem from "@mui/material/MenuItem";
import Select from "@mui/material/Select";
import Typography from "@mui/material/Typography";
import type { ReactNode } from "react";

/** One option in a `ChoiceField`. */
export interface ChoiceOption<T extends string> {
  value: T;
  name: string;
  /** A sentence below it, when the name alone does not say what happens. */
  hint?: string;
  symbol: ReactNode;
}

/** A named choice: heading, explanatory sentence, select field. */
export function ChoiceField<T extends string>({
  heading,
  description,
  value,
  options,
  onChoose,
  ariaLabel,
}: {
  heading: string;
  description: string;
  value: T;
  options: readonly ChoiceOption<T>[];
  onChoose: (chosen: T) => void;
  /** For screen readers: the heading is a text, not a label. */
  ariaLabel: string;
}) {
  const current = options.find((option) => option.value === value);

  return (
    <Box sx={{ display: "grid", gap: 1 }}>
      <Box>
        <Typography variant="subtitle2" sx={{ fontWeight: 660 }}>
          {heading}
        </Typography>
        <Typography variant="caption" color="text.secondary" component="div">
          {description}
        </Typography>
      </Box>

      <Select
        value={value}
        size="small"
        fullWidth
        inputProps={{ "aria-label": ariaLabel }}
        onChange={(event) => onChoose(event.target.value as T)}
        // Closed with its symbol, as in the list — `renderValue`, because MUI
        // otherwise shows only the text.
        renderValue={() => (
          <Box sx={{ display: "flex", alignItems: "center", gap: 1.25 }}>
            <Box sx={{ display: "flex", color: "text.secondary" }}>
              {current?.symbol}
            </Box>
            {current?.name ?? ""}
          </Box>
        )}
        sx={{ borderRadius: 2, "& .MuiSelect-select": { py: 1.1 } }}
      >
        {options.map((option) => (
          <MenuItem key={option.value} value={option.value} sx={{ py: 1 }}>
            <ListItemIcon sx={{ minWidth: 36, color: "text.secondary" }}>
              {option.symbol}
            </ListItemIcon>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography variant="body2">{option.name}</Typography>
              {option.hint ? (
                <Typography variant="caption" color="text.secondary" component="div">
                  {option.hint}
                </Typography>
              ) : null}
            </Box>
            {option.value === value ? (
              <CheckIcon fontSize="small" sx={{ ml: 1.5, color: "primary.main" }} />
            ) : null}
          </MenuItem>
        ))}
      </Select>
    </Box>
  );
}
