import CheckIcon from "@mui/icons-material/Check";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import ChevronLeftIcon from "@mui/icons-material/ChevronLeft";
import Box from "@mui/material/Box";
import Divider from "@mui/material/Divider";
import ListItemIcon from "@mui/material/ListItemIcon";
import MenuItem from "@mui/material/MenuItem";
import Typography from "@mui/material/Typography";

import type { Vorliebe } from "./darstellung";
import type { ColorPreference, Sprachvorliebe } from "../../../core/store/preferencesSlice";

/** Welche Ebene des Kontomenüs offen ist. */
export type Menueebene = "haupt" | "darstellung" | "sprache";

/** Eine Vorliebe als eine Menüzeile: „Darstellung ▸ Dunkel". */
export function Vorliebenzeile<T extends string>({
  vorliebe,
  onOeffnen,
}: {
  vorliebe: Vorliebe<T>;
  onOeffnen: () => void;
}) {
  return (
    <MenuItem onClick={onOeffnen}>
      <ListItemIcon>{vorliebe.symbol}</ListItemIcon>
      <Box sx={{ flexGrow: 1 }}>{vorliebe.ueberschrift}</Box>
      <Typography variant="body2" color="text.secondary" sx={{ ml: 2 }}>
        {vorliebe.name}
      </Typography>
      <ChevronRightIcon fontSize="small" sx={{ ml: 0.5, color: "text.disabled" }} />
    </MenuItem>
  );
}

/**
 * Die aufgeklappte Vorliebe — dieselbe Fläche, ein Schritt tiefer. Ein zweites
 * Menü daneben müsste positioniert werden und liefe an schmalen Fenstern über
 * den Rand.
 */
export function Vorliebenauswahl<T extends string>({
  vorliebe,
  onZurueck,
  onFertig,
}: {
  vorliebe: Vorliebe<T>;
  onZurueck: () => void;
  /** Nach einer Wahl schliesst das Menü — die Wirkung ist sofort zu sehen. */
  onFertig: () => void;
}) {
  return (
    <>
      <MenuItem onClick={onZurueck}>
        <ListItemIcon>
          <ChevronLeftIcon fontSize="small" />
        </ListItemIcon>
        <Typography variant="subtitle2" sx={{ fontWeight: 640 }}>
          {vorliebe.ueberschrift}
        </Typography>
      </MenuItem>
      <Divider />

      {vorliebe.moeglichkeiten.map((moeglich) => (
        <MenuItem
          key={moeglich.wert}
          // `menuitemradio`, weil genau eine gelten kann — sonst hört ein
          // Vorleser gleichwertige Befehle statt einer Auswahl.
          role="menuitemradio"
          aria-checked={moeglich.wert === vorliebe.wert}
          selected={moeglich.wert === vorliebe.wert}
          onClick={() => {
            vorliebe.waehle(moeglich.wert);
            onFertig();
          }}
        >
          <ListItemIcon>{moeglich.symbol}</ListItemIcon>
          <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            <Typography variant="body2">{moeglich.name}</Typography>
            {moeglich.hinweis ? (
              <Typography variant="caption" color="text.secondary" component="div">
                {moeglich.hinweis}
              </Typography>
            ) : null}
          </Box>
          {moeglich.wert === vorliebe.wert ? (
            <CheckIcon fontSize="small" sx={{ ml: 1.5, color: "primary.main" }} />
          ) : null}
        </MenuItem>
      ))}
    </>
  );
}

/**
 * Verzweigt mit konkreten Typen: `Vorliebe<A> | Vorliebe<B>` lässt sich nicht
 * an eine generische Komponente reichen, weil `waehle` kontravariant ist.
 */
export function Vorliebenebene({
  ebene,
  darstellung,
  sprache,
  onZurueck,
  onFertig,
}: {
  ebene: Menueebene;
  darstellung: Vorliebe<ColorPreference>;
  sprache: Vorliebe<Sprachvorliebe>;
  onZurueck: () => void;
  onFertig: () => void;
}) {
  if (ebene === "darstellung") {
    return (
      <Vorliebenauswahl vorliebe={darstellung} onZurueck={onZurueck} onFertig={onFertig} />
    );
  }

  if (ebene === "sprache") {
    return <Vorliebenauswahl vorliebe={sprache} onZurueck={onZurueck} onFertig={onFertig} />;
  }

  return null;
}
