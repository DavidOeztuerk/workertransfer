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
  LANGUAGES,
  resolveLanguage,
  languageSet,
  type LanguagePreference,
} from "../../../core/store/preferencesSlice";
import { spracheSpeichern } from "../../../features/auth/store/authThunks";
import { useThemeMode } from "../../hooks/useThemeMode";
import { ChoiceField, type ChoiceOption } from "../ui/ChoiceField";

/**
 * Flags only in the opened list, beside the written-out name. A flag is a
 * country and not a language — as the only mark of recognition it would be
 * wrong.
 */
const FLAGS: Record<string, string> = { de: "🇩🇪", en: "🇬🇧", fr: "🇫🇷" };

/** A preference with its options — for the menu and the settings page. */
export interface Preference<T extends string> {
  heading: string;
  description: string;
  value: T;
  /** What applies right now, as a word. For the row in the menu. */
  name: string;
  symbol: ReactNode;
  options: readonly ChoiceOption<T>[];
  choose: (chosen: T) => void;
}

/**
 * Appearance and language as data — used by the account menu (a compact
 * submenu) and by the settings page (a section with a select field). One
 * source, so that both know the same options.
 */
export function usePreferences(): {
  appearance: Preference<ColorPreference>;
  language: Preference<LanguagePreference>;
} {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const { preference, setPreference } = useThemeMode();
  const chosenLanguage = useAppSelector((state) => state.preferences.language);
  const signedIn = useAppSelector((state) => state.auth.status === "authenticated");

  const appearanceOptions: ChoiceOption<ColorPreference>[] = [
    {
      value: "system",
      name: t("appearance.system"),
      hint: t("appearance.systemHint"),
      symbol: <DesktopWindowsOutlinedIcon fontSize="small" />,
    },
    { value: "light", name: t("appearance.light"), symbol: <LightModeOutlinedIcon fontSize="small" /> },
    { value: "dark", name: t("appearance.dark"), symbol: <DarkModeOutlinedIcon fontSize="small" /> },
  ];

  const languageOptions: ChoiceOption<LanguagePreference>[] = [
    {
      value: "system",
      name: t("language.system"),
      hint: t("appearance.systemHint"),
      symbol: <LanguageOutlinedIcon fontSize="small" />,
    },
    // A language's name stands in that language.
    ...LANGUAGES.map((language) => ({
      value: language as LanguagePreference,
      name: t(`language.${language}`),
      symbol: (
        <Box component="span" aria-hidden sx={{ fontSize: "1.1rem", lineHeight: 1 }}>
          {FLAGS[language]}
        </Box>
      ),
    })),
  ];

  function chooseLanguage(chosen: LanguagePreference) {
    dispatch(languageSet(chosen));

    // The account gets the resolved language, not "system": a mail must have
    // a language.
    if (signedIn) {
      void dispatch(spracheSpeichern(resolveLanguage(chosen)));
    }
  }

  const currentAppearance = appearanceOptions.find((option) => option.value === preference);
  const currentLanguage = languageOptions.find((option) => option.value === chosenLanguage);

  return {
    appearance: {
      heading: t("appearance.title"),
      description: t("appearance.lead"),
      value: preference,
      name: currentAppearance?.name ?? "",
      symbol: currentAppearance?.symbol,
      options: appearanceOptions,
      choose: setPreference,
    },
    language: {
      heading: t("language.title"),
      description: t("language.lead"),
      value: chosenLanguage,
      name: currentLanguage?.name ?? "",
      symbol: <LanguageOutlinedIcon fontSize="small" />,
      options: languageOptions,
      choose: chooseLanguage,
    },
  };
}

/** The detailed version for the settings page. */
export function AppearanceSections() {
  const { appearance, language } = usePreferences();

  return (
    <Box sx={{ display: "grid", gap: 3, maxWidth: 420 }}>
      <ChoiceField
        heading={appearance.heading}
        description={appearance.description}
        ariaLabel={appearance.heading}
        value={appearance.value}
        options={appearance.options}
        onChoose={appearance.choose}
      />
      <ChoiceField
        heading={language.heading}
        description={language.description}
        ariaLabel={language.heading}
        value={language.value}
        options={language.options}
        onChoose={language.choose}
      />
    </Box>
  );
}
