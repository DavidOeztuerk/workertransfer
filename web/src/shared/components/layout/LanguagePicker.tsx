import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import {
  SPRACHEN,
  languageSet,
  type Sprachvorliebe,
} from "../../../core/store/preferencesSlice";

/**
 * Die Sprachwahl im Kopf.
 *
 * <strong>„Wie mein Gerät" steht als eigener Eintrag drin</strong>, nicht nur
 * als Startwert — sonst gäbe es keinen Weg zurück. Wer einmal Französisch wählt,
 * müsste sonst für immer wählen, auch wenn er das Gerät später umstellt
 * (ADR-0031).
 */
export function LanguagePicker() {
  const dispatch = useAppDispatch();
  const vorliebe = useAppSelector((state) => state.preferences.language);
  const { t } = useTranslation();

  return (
    <TextField
      select
      size="small"
      label={t("sprache.label")}
      value={vorliebe}
      onChange={(ereignis) => dispatch(languageSet(ereignis.target.value as Sprachvorliebe))}
      sx={{ minWidth: 150 }}
    >
      <MenuItem value="system">{t("sprache.system")}</MenuItem>
      {SPRACHEN.map((sprache) => (
        // Der Name der Sprache steht IN dieser Sprache: wer die Oberfläche
        // gerade nicht lesen kann, sucht „Deutsch", nicht „German".
        <MenuItem key={sprache} value={sprache}>
          {t(`sprache.${sprache}`)}
        </MenuItem>
      ))}
    </TextField>
  );
}
