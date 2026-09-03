import DarkModeIcon from "@mui/icons-material/DarkMode";
import LightModeIcon from "@mui/icons-material/LightMode";
import SettingsIcon from "@mui/icons-material/Settings";
import Box from "@mui/material/Box";
import Divider from "@mui/material/Divider";
import IconButton from "@mui/material/IconButton";
import ListItemIcon from "@mui/material/ListItemIcon";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { useState } from "react";
import { useTranslation } from "react-i18next";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import {
  SPRACHEN,
  aufgeloest,
  languageSet,
  type Sprachvorliebe,
} from "../../../core/store/preferencesSlice";
import { spracheSpeichern } from "../../../features/auth/store/authThunks";
import { useThemeMode } from "../../hooks/useThemeMode";

/**
 * Darstellung und Sprache — in EINEM Menü, hinter einem Zahnrad.
 *
 * <strong>Warum nicht mehr im Fuss.</strong> Dort war es logisch (man stellt es
 * einmal ein) und praktisch falsch: wer die Sprache wechseln will, weil er die
 * Oberfläche nicht lesen kann, scrollt nicht erst an das Ende einer Seite. Und
 * ein Auswahlfeld in der Kopfzeile nahm die halbe Breite — auf einem schmalen
 * Bildschirm blieb daneben kein Platz mehr für die Navigation.
 *
 * <strong>Warum ein Menü und kein zweiter Knopf.</strong> Zwei Bedienelemente
 * für zwei Vorlieben sind zwei Dinge, die auf jedem Bildschirm Platz brauchen.
 * Ein Zahnrad ist eines und wächst mit: was später dazukommt (Schriftgrösse,
 * Kontrast), steht daneben statt daneben zu drängen.
 *
 * <strong>Die Sprachen stehen als Einträge, nicht als Auswahlfeld.</strong> In
 * einem Menü ist ein Auswahlfeld ein Fremdkörper: es öffnet ein zweites
 * Overlay über dem ersten. Vier Einträge sind ohnehin kürzer als die Liste, die
 * ein Auswahlfeld aufklappt.
 */
export function SettingsMenu() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const { mode, toggle } = useThemeMode();
  const vorliebe = useAppSelector((state) => state.preferences.language);
  const signedIn = useAppSelector((state) => state.auth.status === "authenticated");
  const [anker, setAnker] = useState<null | HTMLElement>(null);

  function waehle(gewaehlt: Sprachvorliebe) {
    dispatch(languageSet(gewaehlt));

    // Ans Konto geht die AUFGELÖSTE Sprache, nicht „system": der Server hat kein
    // Gerät, dem er folgen könnte, und eine Mail muss eine Sprache haben.
    if (signedIn) {
      void dispatch(spracheSpeichern(aufgeloest(gewaehlt)));
    }
  }

  const helligkeit = t(mode === "light" ? "kopf.dunkel" : "kopf.hell");

  return (
    <>
      <Tooltip title={t("kopf.einstellungenMenue")}>
        <IconButton
          onClick={(event) => setAnker(event.currentTarget)}
          aria-label={t("kopf.einstellungenMenue")}
          aria-haspopup="menu"
          aria-expanded={anker !== null}
          size="small"
          // Die gefüllte Fassung, nicht die umrissene: als 20px-Symbol ist der
          // Umriss eine Haarlinie und verschwindet neben dem Text daneben.
          sx={{ color: "text.secondary" }}
        >
          <SettingsIcon />
        </IconButton>
      </Tooltip>

      <Menu
        anchorEl={anker}
        open={anker !== null}
        onClose={() => setAnker(null)}
        slotProps={{ paper: { sx: { minWidth: 232, mt: 1 } } }}
      >
        <MenuItem
          onClick={() => {
            toggle();
            setAnker(null);
          }}
        >
          <ListItemIcon>
            {mode === "light" ? (
              <DarkModeIcon fontSize="small" />
            ) : (
              <LightModeIcon fontSize="small" />
            )}
          </ListItemIcon>
          {helligkeit}
        </MenuItem>

        <Divider />

        <Box sx={{ px: 2, pt: 1, pb: 0.5 }}>
          <Typography
            variant="caption"
            sx={{ fontWeight: 660, letterSpacing: "0.06em", textTransform: "uppercase" }}
            color="text.secondary"
          >
            {t("sprache.label")}
          </Typography>
        </Box>

        <Sprachzeile
          wert="system"
          gewaehlt={vorliebe}
          beschriftung={t("sprache.system")}
          onWahl={(wahl) => {
            waehle(wahl);
            setAnker(null);
          }}
        />
        {SPRACHEN.map((sprache) => (
          // Der Name der Sprache steht IN dieser Sprache: wer die Oberfläche
          // gerade nicht lesen kann, sucht „Deutsch", nicht „German".
          <Sprachzeile
            key={sprache}
            wert={sprache}
            gewaehlt={vorliebe}
            beschriftung={t(`sprache.${sprache}`)}
            onWahl={(wahl) => {
              waehle(wahl);
              setAnker(null);
            }}
          />
        ))}
      </Menu>
    </>
  );
}

/**
 * Eine Sprache im Menü.
 *
 * `aria-checked` und `role="menuitemradio"`, weil es genau eine sein kann —
 * ohne das hört ein Vorleser vier gleichwertige Befehle und nicht eine Wahl.
 */
function Sprachzeile({
  wert,
  gewaehlt,
  beschriftung,
  onWahl,
}: {
  wert: Sprachvorliebe;
  gewaehlt: Sprachvorliebe;
  beschriftung: string;
  onWahl: (wahl: Sprachvorliebe) => void;
}) {
  return (
    <MenuItem
      role="menuitemradio"
      aria-checked={wert === gewaehlt}
      selected={wert === gewaehlt}
      onClick={() => onWahl(wert)}
    >
      {beschriftung}
    </MenuItem>
  );
}
