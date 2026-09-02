import DarkModeIcon from "@mui/icons-material/DarkModeOutlined";
import LightModeIcon from "@mui/icons-material/LightModeOutlined";
import IconButton from "@mui/material/IconButton";
import Tooltip from "@mui/material/Tooltip";

import { useThemeMode } from "../../hooks/useThemeMode";

/**
 * Der Umschalter für hell und dunkel.
 *
 * Beschriftet, nicht nur bebildert: ein Symbol allein sagt einem Screenreader
 * nichts, und dieselbe Sonne kann "ist hell" oder "schalte auf hell" heissen.
 * Der Text sagt, was PASSIERT, nicht was gerade gilt.
 */
export function ColorModeToggle() {
  const { mode, toggle } = useThemeMode();
  const ziel = mode === "light" ? "dunkle" : "helle";

  return (
    <Tooltip title={`Auf ${ziel} Darstellung wechseln`}>
      <IconButton onClick={toggle} aria-label={`Auf ${ziel} Darstellung wechseln`} size="small">
        {mode === "light" ? <DarkModeIcon fontSize="small" /> : <LightModeIcon fontSize="small" />}
      </IconButton>
    </Tooltip>
  );
}
