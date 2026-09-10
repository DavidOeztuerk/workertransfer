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
 * Fahnen nur in der aufgeklappten Liste, neben dem ausgeschriebenen Namen.
 * Eine Fahne ist ein Land und keine Sprache — als einziges Erkennungszeichen
 * wäre sie falsch.
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
 * Darstellung und Sprache als Daten — verwendet vom Kontomenü (knappes
 * Untermenü) und von der Einstellungsseite (Abschnitt mit Auswahlfeld). Eine
 * Quelle, damit beide dieselben Möglichkeiten kennen.
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
    // Der Name der Sprache steht in dieser Sprache.
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

    // Ans Konto geht die aufgelöste Sprache, nicht „system": eine Mail muss
    // eine Sprache haben.
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
      symbol: <LanguageOutlinedIcon fontSize="small" />,
      moeglichkeiten: sprachWahl,
      waehle: waehleSprache,
    },
  };
}

/** Die ausführliche Fassung für die Einstellungsseite. */
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
