import ClearIcon from "@mui/icons-material/ClearOutlined";
import TuneIcon from "@mui/icons-material/TuneOutlined";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Divider from "@mui/material/Divider";
import Paper from "@mui/material/Paper";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";

import {
  EMPLOYMENT_TYPES,
  type EmploymentType,
  REMOTE_MODES,
  type RemoteMode,
  employmentLabel,
  remoteLabel,
} from "../api/jobs";

/** Was gefiltert werden kann. Leer heisst überall „egal". */
export interface Stellenfilter {
  q: string;
  location: string;
  remote: RemoteMode | "";
  employment: EmploymentType | "";
  skills: string;
}

export const LEERER_FILTER: Stellenfilter = {
  q: "",
  location: "",
  remote: "",
  employment: "",
  skills: "",
};

/** Wie viele Filter gerade etwas tun. Für die Anzeige am Kopf. */
export function aktiveFilter(filter: Stellenfilter): number {
  return Object.values(filter).filter((wert) => wert.trim() !== "").length;
}

/**
 * Die Filter als Seitenleiste.
 *
 * <strong>Untereinander und nicht in einer Zeile</strong>, und das ist der
 * eigentliche Gewinn: die Filter standen vorher als fünf Felder nebeneinander
 * über der Liste, wurden auf jedem schmalen Bildschirm umgebrochen und schoben
 * die Ergebnisse unter die Falzkante. Eine Spalte hat Platz für Beschriftung
 * UND Hinweis, und die Liste beginnt oben.
 *
 * <strong>Gefiltert wird erst beim Absenden.</strong> Kein Filtern beim Tippen:
 * jede Taste wäre eine Anfrage, die Liste spränge unter der Hand, und wer
 * „Kubernetes" tippt, hätte unterwegs elf Ergebnislisten gesehen, die ihn nicht
 * interessieren.
 */
export function JobFilterSidebar({
  entwurf,
  onChange,
  onSubmit,
  onReset,
}: {
  entwurf: Stellenfilter;
  onChange: (filter: Stellenfilter) => void;
  onSubmit: () => void;
  onReset: () => void;
}) {
  const { t } = useTranslation();
  const anzahl = aktiveFilter(entwurf);

  return (
    <Paper
      component="form"
      variant="outlined"
      onSubmit={(ereignis: React.FormEvent) => {
        ereignis.preventDefault();
        onSubmit();
      }}
      sx={{
        p: 2.5,
        borderRadius: 3,
        // Klebt beim Blättern mit — die Filter bleiben erreichbar, ohne dass
        // jemand nach oben scrollen muss.
        position: { md: "sticky" },
        top: { md: 24 },
      }}
    >
      <Box sx={{ display: "flex", alignItems: "center", gap: 1, mb: 2 }}>
        <TuneIcon fontSize="small" color="action" />
        <Typography variant="h4" sx={{ flexGrow: 1 }}>
          {t("stellen.filter")}
        </Typography>
        {anzahl > 0 ? (
          <Button
            size="small"
            variant="text"
            startIcon={<ClearIcon fontSize="small" />}
            onClick={onReset}
          >
            {t("stellen.filterZuruecksetzen")}
          </Button>
        ) : null}
      </Box>

      {anzahl > 0 ? (
        <Typography variant="caption" color="text.secondary" sx={{ display: "block", mb: 2 }}>
          {t("stellen.aktiveFilter", { count: anzahl })}
        </Typography>
      ) : null}

      <Divider sx={{ mb: 2.5 }} />

      <Box sx={{ display: "grid", gap: 2.5 }}>
        <TextField
          label={t("stellen.suchbegriff")}
          placeholder={t("stellen.suchbegriffBeispiel")}
          value={entwurf.q}
          onChange={(ereignis) => onChange({ ...entwurf, q: ereignis.target.value })}
        />

        <TextField
          label={t("stellen.faehigkeiten")}
          placeholder={t("stellen.faehigkeitenBeispiel")}
          helperText={t("stellen.faehigkeitenHinweis")}
          value={entwurf.skills}
          onChange={(ereignis) => onChange({ ...entwurf, skills: ereignis.target.value })}
        />

        <TextField
          label={t("stellen.ort")}
          value={entwurf.location}
          onChange={(ereignis) => onChange({ ...entwurf, location: ereignis.target.value })}
        />

        <TextField
          select
          label={t("stellen.arbeitsform")}
          value={entwurf.remote}
          slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
          onChange={(ereignis) =>
            onChange({ ...entwurf, remote: ereignis.target.value as RemoteMode | "" })
          }
        >
          <option value="">{t("stellen.egal")}</option>
          {REMOTE_MODES.map((wert) => (
            <option key={wert} value={wert}>
              {remoteLabel(wert)}
            </option>
          ))}
        </TextField>

        <TextField
          select
          label={t("stellen.beschaeftigungsart")}
          value={entwurf.employment}
          slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
          onChange={(ereignis) =>
            onChange({ ...entwurf, employment: ereignis.target.value as EmploymentType | "" })
          }
        >
          <option value="">{t("stellen.egal")}</option>
          {EMPLOYMENT_TYPES.map((wert) => (
            <option key={wert} value={wert}>
              {employmentLabel(wert)}
            </option>
          ))}
        </TextField>

        <Button type="submit" variant="contained" fullWidth>
          {t("stellen.suchen")}
        </Button>
      </Box>
    </Paper>
  );
}
