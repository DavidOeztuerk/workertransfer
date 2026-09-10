import DarkModeOutlinedIcon from "@mui/icons-material/DarkModeOutlined";
import DesktopWindowsOutlinedIcon from "@mui/icons-material/DesktopWindowsOutlined";
import LanguageOutlinedIcon from "@mui/icons-material/LanguageOutlined";
import LightModeOutlinedIcon from "@mui/icons-material/LightModeOutlined";
import Box from "@mui/material/Box";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import type { ColorPreference } from "../../../core/store/preferencesSlice";
import {
  SPRACHEN,
  aufgeloest,
  languageSet,
  type Sprachvorliebe,
} from "../../../core/store/preferencesSlice";
import { spracheSpeichern } from "../../../features/auth/store/authThunks";
import { useThemeMode } from "../../hooks/useThemeMode";
import { WahlFeld, type Wahlmoeglichkeit } from "../ui/WahlFeld";

/**
 * Die Fahne steht bei der SPRACHE in der Liste — und nie am zugeklappten Feld.
 *
 * <strong>Eine Fahne ist ein Land und keine Sprache.</strong> Deutsch spricht
 * auch, wer in Wien oder Bern lebt; Englisch ist nicht Grossbritannien. Als
 * einziges Erkennungszeichen wäre sie deshalb falsch. In der aufgeklappten
 * Liste steht sie neben dem ausgeschriebenen Namen und hilft beim schnellen
 * Finden — zugeklappt trägt das Feld eine Weltkugel, die keine Herkunft
 * behauptet.
 */
const FAHNEN: Record<string, string> = { de: "🇩🇪", en: "🇬🇧", fr: "🇫🇷" };

/** Eine Vorliebe samt ihren Möglichkeiten — für Menü und Einstellungsseite. */
export interface Vorliebe<T extends string> {
  ueberschrift: string;
  beschreibung: string;
  wert: T;
  /** Was gerade gilt, als Wort. Für die Zeile im Menü. */
  name: string;
  symbol: ReactNode;
  moeglichkeiten: readonly Wahlmoeglichkeit<T>[];
  waehle: (gewaehlt: T) => void;
}

/**
 * Darstellung und Sprache — die Daten, nicht die Darstellung.
 *
 * <strong>Sie stehen hier, weil sie an ZWEI Stellen gebraucht werden</strong>,
 * und zwar in zwei verschiedenen Formen: im Kontomenü als knappes Untermenü
 * („Darstellung ▸ Dunkel"), auf der Einstellungsseite als Abschnitt mit
 * Überschrift und erklärendem Satz. Beides aus einer Quelle, sonst kennt das
 * eine beim nächsten Eintrag eine Möglichkeit, die das andere nicht hat.
 *
 * <strong>Die Darstellung hat DREI Werte.</strong> Der Store kennt
 * `system | light | dark` seit jeher, das alte Zahnrad rief aber nur ein
 * zweiwertiges `toggle()` — „System" war vorhanden und über die Oberfläche
 * nicht erreichbar. Wer einmal umschaltete, kam nie wieder zurück zu „folgt dem
 * Gerät", ohne den lokalen Speicher zu leeren.
 */
export function useVorlieben(): {
  darstellung: Vorliebe<ColorPreference>;
  sprache: Vorliebe<Sprachvorliebe>;
} {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const { preference, setPreference } = useThemeMode();
  const gewaehlteSprache = useAppSelector((state) => state.preferences.language);
  const signedIn = useAppSelector((state) => state.auth.status === "authenticated");

  const darstellungsWahl: Wahlmoeglichkeit<ColorPreference>[] = [
    {
      wert: "system",
      name: t("darstellung.system"),
      hinweis: t("darstellung.systemHinweis"),
      symbol: <DesktopWindowsOutlinedIcon fontSize="small" />,
    },
    { wert: "light", name: t("darstellung.hell"), symbol: <LightModeOutlinedIcon fontSize="small" /> },
    { wert: "dark", name: t("darstellung.dunkel"), symbol: <DarkModeOutlinedIcon fontSize="small" /> },
  ];

  const sprachWahl: Wahlmoeglichkeit<Sprachvorliebe>[] = [
    {
      wert: "system",
      name: t("sprache.system"),
      hinweis: t("darstellung.systemHinweis"),
      symbol: <LanguageOutlinedIcon fontSize="small" />,
    },
    // Der Name der Sprache steht IN dieser Sprache: wer die Oberfläche gerade
    // nicht lesen kann, sucht „Deutsch", nicht „German".
    ...SPRACHEN.map((wert) => ({
      wert: wert as Sprachvorliebe,
      name: t(`sprache.${wert}`),
      symbol: (
        <Box component="span" aria-hidden sx={{ fontSize: "1.1rem", lineHeight: 1 }}>
          {FAHNEN[wert]}
        </Box>
      ),
    })),
  ];

  function waehleSprache(gewaehlt: Sprachvorliebe) {
    dispatch(languageSet(gewaehlt));

    // Ans Konto geht die AUFGELÖSTE Sprache, nicht „system": der Server hat kein
    // Gerät, dem er folgen könnte, und eine Mail muss eine Sprache haben.
    if (signedIn) {
      void dispatch(spracheSpeichern(aufgeloest(gewaehlt)));
    }
  }

  const jetztDarstellung = darstellungsWahl.find((m) => m.wert === preference);
  const jetztSprache = sprachWahl.find((m) => m.wert === gewaehlteSprache);

  return {
    darstellung: {
      ueberschrift: t("darstellung.titel"),
      beschreibung: t("darstellung.lead"),
      wert: preference,
      name: jetztDarstellung?.name ?? "",
      symbol: jetztDarstellung?.symbol,
      moeglichkeiten: darstellungsWahl,
      waehle: setPreference,
    },
    sprache: {
      ueberschrift: t("sprache.titel"),
      beschreibung: t("sprache.lead"),
      wert: gewaehlteSprache,
      name: jetztSprache?.name ?? "",
      // Zugeklappt eine Weltkugel und keine Fahne — siehe oben.
      symbol: <LanguageOutlinedIcon fontSize="small" />,
      moeglichkeiten: sprachWahl,
      waehle: waehleSprache,
    },
  };
}

/**
 * Darstellung und Sprache als zwei benannte Abschnitte — für die
 * Einstellungsseite.
 *
 * <strong>Hier steht die ausführliche Fassung, im Kontomenü die knappe.</strong>
 * Ein Menü ist eine Liste von Wegen; ein Abschnitt mit Überschrift, erklärendem
 * Satz und Auswahlfeld ist eine Seite. Beides in dasselbe Menü zu legen stapelt
 * zwei Bedienarten übereinander: Einträge, die einen wegbringen, und Felder,
 * die einen dabehalten.
 */
export function DarstellungsAbschnitte() {
  const { darstellung, sprache } = useVorlieben();

  return (
    <Box sx={{ display: "grid", gap: 3, maxWidth: 420 }}>
      <WahlFeld
        ueberschrift={darstellung.ueberschrift}
        beschreibung={darstellung.beschreibung}
        etikett={darstellung.ueberschrift}
        wert={darstellung.wert}
        moeglichkeiten={darstellung.moeglichkeiten}
        onWahl={darstellung.waehle}
      />
      <WahlFeld
        ueberschrift={sprache.ueberschrift}
        beschreibung={sprache.beschreibung}
        etikett={sprache.ueberschrift}
        wert={sprache.wert}
        moeglichkeiten={sprache.moeglichkeiten}
        onWahl={sprache.waehle}
      />
    </Box>
  );
}
