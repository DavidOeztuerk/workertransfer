import DarkModeIcon from "@mui/icons-material/DarkModeOutlined";
import LightModeIcon from "@mui/icons-material/LightModeOutlined";
import IconButton from "@mui/material/IconButton";
import Tooltip from "@mui/material/Tooltip";

import { useTranslation } from "react-i18next";

import { useThemeMode } from "../../hooks/useThemeMode";

/**
 * Der Umschalter für hell und dunkel.
 *
 * Beschriftet, nicht nur bebildert: ein Symbol allein sagt einem Screenreader
 * nichts, und dieselbe Sonne kann "ist hell" oder "schalte auf hell" heissen.
 * Der Text sagt, was PASSIERT, nicht was gerade gilt.
 */
export function ColorModeToggle() {
  const { t } = useTranslation();
  const { mode, toggle } = useThemeMode();
  const beschriftung = t(mode === "light" ? "kopf.dunkel" : "kopf.hell");

  return (
    <Tooltip title={beschriftung}>
      <IconButton onClick={toggle} aria-label={beschriftung} size="small">
        {mode === "light" ? <DarkModeIcon fontSize="small" /> : <LightModeIcon fontSize="small" />}
      </IconButton>
    </Tooltip>
  );
}
