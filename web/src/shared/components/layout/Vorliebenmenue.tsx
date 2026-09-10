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

/**
 * Eine Vorliebe als EINE Zeile im Menü: „Darstellung ▸ Dunkel".
 *
 * <strong>Kein Auswahlfeld im Menü.</strong> Ein Menü ist eine Liste von
 * Einträgen, die man anklickt; ein Auswahlfeld darin öffnet ein zweites Overlay
 * über dem ersten und stapelt damit zwei Bedienarten übereinander. Diese Zeile
 * verhält sich wie jede andere — und zeigt nebenbei, was gerade gilt, ohne dass
 * man sie öffnen muss.
 */
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
      {/* Was gilt, steht rechts — grau, damit es die Zeile nicht mit dem
          Eintragsnamen um die Aufmerksamkeit bringt. */}
      <Typography variant="body2" color="text.secondary" sx={{ ml: 2 }}>
        {vorliebe.name}
      </Typography>
      <ChevronRightIcon fontSize="small" sx={{ ml: 0.5, color: "text.disabled" }} />
    </MenuItem>
  );
}

/**
 * Die aufgeklappte Vorliebe — dieselbe Fläche, ein Schritt tiefer.
 *
 * <strong>Hineingehen statt aufklappen.</strong> Ein zweites Menü neben dem
 * ersten muss positioniert werden, läuft an schmalen Fenstern über den Rand und
 * bringt die Tastatursteuerung durcheinander. Hier tauscht dieselbe Fläche
 * ihren Inhalt: das Menü bleibt, wo es war, und ein „Zurück" führt heraus.
 */
export function Vorliebenauswahl<T extends string>({
  vorliebe,
  onZurueck,
}: {
  vorliebe: Vorliebe<T>;
  onZurueck: () => void;
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
          // `menuitemradio` und `aria-checked`, weil es genau eine sein kann —
          // ohne die Rolle hört ein Vorleser vier gleichwertige Befehle statt
          // einer Auswahl.
          role="menuitemradio"
          aria-checked={moeglich.wert === vorliebe.wert}
          selected={moeglich.wert === vorliebe.wert}
          onClick={() => {
            vorliebe.waehle(moeglich.wert);
            onZurueck();
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
          {/* Das Häkchen steht rechts, nicht links: links steht das Symbol der
              Möglichkeit, und ein zweites Zeichen davor verschöbe alle Zeilen
              gegeneinander, je nachdem, welche gerade gilt. */}
          {moeglich.wert === vorliebe.wert ? (
            <CheckIcon fontSize="small" sx={{ ml: 1.5, color: "primary.main" }} />
          ) : null}
        </MenuItem>
      ))}
    </>
  );
}

/**
 * Die offene Vorliebe — mit KONKRETEN Typen statt einer Vereinigung.
 *
 * `Vorliebe<ColorPreference> | Vorliebe<Sprachvorliebe>` lässt sich nicht an
 * eine generische Komponente reichen: `waehle` ist kontravariant, und die
 * Vereinigung hiesse „nimm eine Funktion, die JEDEN der sechs Werte annimmt".
 * Die gibt es nicht. Also wird hier verzweigt, wo beide Typen noch bekannt sind.
 */
export function Vorliebenebene({
  ebene,
  darstellung,
  sprache,
  onZurueck,
}: {
  ebene: Menueebene;
  darstellung: Vorliebe<ColorPreference>;
  sprache: Vorliebe<Sprachvorliebe>;
  onZurueck: () => void;
}) {
  if (ebene === "darstellung") {
    return <Vorliebenauswahl vorliebe={darstellung} onZurueck={onZurueck} />;
  }

  if (ebene === "sprache") {
    return <Vorliebenauswahl vorliebe={sprache} onZurueck={onZurueck} />;
  }

  return null;
}
