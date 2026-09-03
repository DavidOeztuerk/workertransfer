import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import {
  SPRACHEN,
  aufgeloest,
  languageSet,
  type Sprachvorliebe,
} from "../../../core/store/preferencesSlice";
import { spracheSpeichern } from "../../../features/auth/store/authThunks";

/**
 * Die Sprachwahl im Kopf.
 *
 * <strong>„Wie mein Gerät" steht als eigener Eintrag drin</strong>, nicht nur
 * als Startwert — sonst gäbe es keinen Weg zurück. Wer einmal Französisch wählt,
 * müsste sonst für immer wählen, auch wenn er das Gerät später umstellt
 * (ADR-0031).
 *
 * <strong>Die Wahl geht zweimal hin</strong>, und die beiden Ziele beantworten
 * verschiedene Fragen. Der lokale Speicher trägt sie durch das Neuladen, auch
 * ohne Konto. Das KONTO trägt sie in die Mails: eine Löschbestätigung wird Tage
 * später von einem Zusteller geschrieben, ohne Anfrage und ohne Browser — dort
 * gibt es nur die Zeile. Wer abgemeldet wählt, ändert deshalb nur das Erste.
 */
export function LanguagePicker() {
  const dispatch = useAppDispatch();
  const vorliebe = useAppSelector((state) => state.preferences.language);
  const angemeldet = useAppSelector((state) => state.auth.status === "authenticated");
  const { t } = useTranslation();

  function waehle(gewaehlt: Sprachvorliebe) {
    dispatch(languageSet(gewaehlt));

    // Ans Konto geht die AUFGELÖSTE Sprache, nicht „system": der Server hat
    // kein Gerät, dem er folgen könnte, und eine Mail muss eine Sprache haben.
    if (angemeldet) {
      void dispatch(spracheSpeichern(aufgeloest(gewaehlt)));
    }
  }

  return (
    <TextField
      select
      // NATIV, wie beim Unternehmenswähler und aus demselben Grund: MUIs
      // Voreinstellung ist ein Listenfeld aus `div`s, das weder ein
      // Screenreader noch `selectOption` als Auswahlfeld bedient. Ausgerechnet
      // die Sprachwahl unbedienbar zu machen wäre die schlechteste Stelle
      // dafür — sie ist der Weg heraus für den, der die Oberfläche gerade
      // nicht lesen kann.
      slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
      size="small"
      label={t("sprache.label")}
      value={vorliebe}
      onChange={(ereignis) => waehle(ereignis.target.value as Sprachvorliebe)}
      sx={{ minWidth: 150 }}
    >
      <option value="system">{t("sprache.system")}</option>
      {SPRACHEN.map((sprache) => (
        // Der Name der Sprache steht IN dieser Sprache: wer die Oberfläche
        // gerade nicht lesen kann, sucht „Deutsch", nicht „German".
        <option key={sprache} value={sprache}>
          {t(`sprache.${sprache}`)}
        </option>
      ))}
    </TextField>
  );
}
