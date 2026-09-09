import ClearIcon from "@mui/icons-material/ClearOutlined";
import MyLocationIcon from "@mui/icons-material/MyLocation";
import TuneIcon from "@mui/icons-material/TuneOutlined";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Divider from "@mui/material/Divider";
import FormHelperText from "@mui/material/FormHelperText";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Paper from "@mui/material/Paper";
import TextField from "@mui/material/TextField";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";

import type { Ortung } from "../../../shared/hooks/useGeolocation";
import { radius } from "../../../styles/tokens/spacing";

import {
  EMPLOYMENT_TYPES,
  type EmploymentType,
  REMOTE_MODES,
  type RemoteMode,
  employmentLabel,
  remoteLabel,
} from "../api/jobs";

/**
 * Die anwählbaren Entfernungen, in Kilometern.
 *
 * Unter zehn steht keine: die Koordinaten der Anzeigen sind Stadtmittelpunkte,
 * und ein Radius unter der Ausdehnung einer Stadt behauptete eine Genauigkeit,
 * die die Daten nicht haben. Dieselbe Zahl steht als `Umkreis.KleinsterRadiusKm`
 * im Dienst und wird dort auch durchgesetzt — hier ist sie die Auswahl, dort
 * die Regel.
 */
export const ENTFERNUNGEN = [10, 25, 50, 100, 200] as const;

/** Was gefiltert werden kann. Leer heisst überall „egal". */
export interface Stellenfilter {
  q: string;
  location: string;
  remote: RemoteMode | "";
  employment: EmploymentType | "";
  skills: string;
  /**
   * Der Radius in Kilometern, als Zeichenkette wie jedes andere Auswahlfeld.
   *
   * Der Standort steht bewusst NICHT hier: er ist keine Eingabe in dieses
   * Formular, sondern eine Erlaubnis, die der Browser einholt — und er wird
   * nirgends gespeichert (siehe `useGeolocation`).
   */
  radius: string;
}

export const LEERER_FILTER: Stellenfilter = {
  q: "",
  location: "",
  remote: "",
  employment: "",
  skills: "",
  radius: "",
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
  ortung,
}: {
  entwurf: Stellenfilter;
  onChange: (filter: Stellenfilter) => void;
  onSubmit: () => void;
  onReset: () => void;
  ortung: Ortung;
}) {
  const { t } = useTranslation();
  const anzahl = aktiveFilter(entwurf);
  const hatStandort = ortung.standort !== null;

  /*
   * EINE MITTE GENÜGT, und der getippte Ort ist auch eine.
   *
   * Vorher hing die Entfernung allein am Ortungsknopf. Wer „Leipzig" tippt und
   * „50 km" will, meint aber „um Leipzig herum" — und braucht dafür weder GPS
   * noch die Erlaubnis dazu. Der Dienst löst den Ortsnamen mit derselben
   * Tabelle auf, mit der er auch die Anzeigen verortet (ADR-0032).
   */
  const hatMitte = hatStandort || entwurf.location.trim() !== "";

  /*
    BEI „VOLLSTÄNDIG REMOTE" GIBT ES KEINE ENTFERNUNG.
   
    Nicht Geschmack, sondern nachweisbar wirkungslos: der Dienst lässt voll
    remote ausgeschriebene Stellen unabhängig vom Radius durch (ADR-0032). Die
    Kombination „nur remote" + „25 km" liefert also dasselbe wie „nur remote"
    allein — ein Bedienelement, das sichtbar dasteht und provably nichts tut,
    ist schlimmer als keines. Dieselbe Regel wie in SkillSwap, dort
    `filters.locationType !== 'remote'`.
  */
  const nurRemote = entwurf.remote === "full";

  // Ein Fehlschlag, sonst die Bestätigung, sonst — falls der Browser gar nicht
  // orten kann — der Grund dafür. Im Ruhezustand nichts.
  const hinweis =
    ortung.fehler ??
    (hatStandort
      ? t("stellen.standortGerundet")
      : ortung.moeglich
        ? null
        : t("standort.nichtMoeglich"));

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
        // DERSELBE Radius wie eine Stellenkarte. Vorher stand hier `3`, was
        // über `theme.shape` dreissig Pixel ergab — die Karten daneben haben
        // vierzehn, und zwei Rundungen nebeneinander sehen aus wie ein
        // Versehen. Der Wert kommt aus demselben Token wie die Karte.
        borderRadius: `${radius.lg}px`,
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
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mb: 2 }}
        >
          {t("stellen.aktiveFilter", { count: anzahl })}
        </Typography>
      ) : null}

      <Divider sx={{ mb: 2.5 }} />

      <Box sx={{ display: "grid", gap: 2.5 }}>
        <TextField
          label={t("stellen.suchbegriff")}
          placeholder={t("stellen.suchbegriffBeispiel")}
          value={entwurf.q}
          onChange={(ereignis) =>
            onChange({ ...entwurf, q: ereignis.target.value })
          }
        />

        {/*
          Ohne Hilfetext unter dem Feld: der Platzhalter („Python, Kubernetes")
          sagt dasselbe kürzer, und drei Zeilen Erklärung unter einem von sechs
          Feldern machen aus einer Filterleiste eine Anleitung.
        */}
        <TextField
          label={t("stellen.faehigkeiten")}
          placeholder={t("stellen.faehigkeitenBeispiel")}
          value={entwurf.skills}
          onChange={(ereignis) =>
            onChange({ ...entwurf, skills: ereignis.target.value })
          }
        />

        <TextField
          select
          label={t("stellen.arbeitsform")}
          value={entwurf.remote}
          onChange={(ereignis) => {
            const gewaehlt = ereignis.target.value as RemoteMode | "";

            // Der Radius geht MIT, wenn „vollständig remote" gewählt wird.
            // Bliebe er stehen, zählte er unten als aktiver Filter und wäre
            // keiner — und er käme beim Zurückschalten unbemerkt wieder.
            onChange({
              ...entwurf,
              remote: gewaehlt,
              radius: gewaehlt === "full" ? "" : entwurf.radius,
            });
          }}
        >
          <MenuItem value="">{t("stellen.egal")}</MenuItem>
          {REMOTE_MODES.map((wert) => (
            <MenuItem key={wert} value={wert}>
              {remoteLabel(wert)}
            </MenuItem>
          ))}
        </TextField>

        <TextField
          select
          label={t("stellen.beschaeftigungsart")}
          value={entwurf.employment}
          onChange={(ereignis) =>
            onChange({
              ...entwurf,
              employment: ereignis.target.value as EmploymentType | "",
            })
          }
        >
          <MenuItem value="">{t("stellen.egal")}</MenuItem>
          {EMPLOYMENT_TYPES.map((wert) => (
            <MenuItem key={wert} value={wert}>
              {employmentLabel(wert)}
            </MenuItem>
          ))}
        </TextField>

        {/*
          ORT UND ENTFERNUNG SIND EINE GRUPPE, und sie steht am Ende.

          Vorher stand der Ort zwischen Fähigkeiten und Arbeitsform — also
          getrennt von dem einen Feld, mit dem er zusammen gelesen wird. Beide
          beantworten dieselbe Frage („wo?"), nur auf zwei Arten: als Name und
          als Umkreis. Der engere Abstand innerhalb der Gruppe zeigt das, ohne
          dass eine Überschrift es sagen muss.
        */}
        <Box sx={{ display: "grid", gap: 1.25 }}>
          <TextField
            label={t("stellen.ort")}
            value={entwurf.location}
            onChange={(ereignis) =>
              onChange({ ...entwurf, location: ereignis.target.value })
            }
          />

          {nurRemote ? null : (
            <Box>
              <Box sx={{ display: "flex", alignItems: "flex-end", gap: 1 }}>
                <TextField
                  select
                  sx={{ flexGrow: 1 }}
                  label={t("stellen.entfernung")}
                  value={entwurf.radius}
                  // OHNE Mitte ist dieses Feld tot, und das ist ehrlicher als
                  // eine Auswahl, die man treffen kann und die dann nichts tut:
                  // „25 km" ohne Punkt ist keine Frage, die jemand beantworten
                  // könnte.
                  disabled={!hatMitte}
                  onChange={(ereignis) =>
                    onChange({ ...entwurf, radius: ereignis.target.value })
                  }
                >
                  <MenuItem value="">{t("stellen.egal")}</MenuItem>
                  {ENTFERNUNGEN.map((km) => (
                    <MenuItem key={km} value={String(km)}>
                      {t("stellen.kilometer", { km })}
                    </MenuItem>
                  ))}
                </TextField>

                <Tooltip
                  title={
                    hatStandort
                      ? t("stellen.standortVergessen")
                      : t("stellen.standortHolenHinweis")
                  }
                >
                  {/* Der Rahmen ist nötig: ein abgeschalteter Knopf sendet keine
                  Zeigerereignisse, und der Hinweis erschiene nie — gerade
                  dann, wenn er am meisten erklärte. */}
                  <Box component="span">
                    <IconButton
                      aria-label={
                        hatStandort
                          ? t("stellen.standortVergessen")
                          : t("stellen.standortHolen")
                      }
                      color={hatStandort ? "primary" : "default"}
                      disabled={!ortung.moeglich || ortung.laedt}
                      onClick={() => {
                        if (!hatStandort) {
                          ortung.frage();
                          return;
                        }

                        // Der Standort geht, und die Entfernung geht mit. Ein
                        // stehengebliebenes „25 km" ohne Punkt sähe wie ein aktiver
                        // Filter aus und wäre keiner.
                        ortung.vergiss();
                        onChange({ ...entwurf, radius: "" });
                      }}
                      sx={{ mb: 0.25 }}
                    >
                      {ortung.laedt ? (
                        <CircularProgress size={20} />
                      ) : (
                        <MyLocationIcon />
                      )}
                    </IconButton>
                  </Box>
                </Tooltip>
              </Box>

              {/*
              NUR wenn es etwas zu sagen gibt.

              Hier stand vorher IMMER ein Satz, im Ruhezustand „Auf Knopfdruck —
              dein Browser fragt dich". Das erklärte ein Knopfsymbol, das sich
              selbst erklärt, und kostete drei Zeilen in einer Leiste mit sechs
              Feldern. Was der Knopf tut und dass die Position gerundet wird,
              steht jetzt in seinem Hinweisfähnchen — abrufbar statt dauernd da.

              Ein Fehlschlag und die Bestätigung bleiben: der eine ist die
              Antwort auf eine Handlung, die andere die Zusage, die diesen Knopf
              vertretbar macht.
            */}
              {hinweis === null ? null : (
                <FormHelperText error={ortung.fehler !== null} sx={{ mx: 0 }}>
                  {hinweis}
                </FormHelperText>
              )}
            </Box>
          )}
        </Box>

        <Button type="submit" variant="contained" fullWidth>
          {t("stellen.suchen")}
        </Button>
      </Box>
    </Paper>
  );
}
