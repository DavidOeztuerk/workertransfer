import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate } from "react-router-dom";

import {
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import type { CompanyProfile } from "../../company/api/companies";
import {
  EMPLOYMENT_TYPES,
  REMOTE_MODES,
  employmentLabel,
  type EmploymentType,
  type Job,
  remoteLabel,
  type RemoteMode,
  type SucheFehlschlag,
  searchJobs,
} from "../api/jobs";
import { getMyProfile } from "../api/profile";
import { Requirements } from "../components/Requirements";
import { merkeStelle } from "../lib/intent";
import { useHandelnder } from "../lib/session";
import { useAsync, useSeiten } from "../lib/useAsync";
import { useFirmenprofile } from "../lib/useFirmenprofile";

interface Filters {
  q: string;
  location: string;
  remote: RemoteMode | "";
  employment: EmploymentType | "";
}

const EMPTY: Filters = { q: "", location: "", remote: "", employment: "" };

/**
 * Die offenen Stellen — die einzige Liste dieser Anwendung, die ohne Konto
 * vollständig ist.
 *
 * Zwei Filterzustände mit Absicht: was im Formular steht und wonach gesucht
 * wurde. Sonst liefe bei jedem Tastendruck eine Abfrage, und die Liste
 * flackerte, während jemand ein Wort tippt.
 */
export function JobsPage() {
  const { t } = useTranslation();
  const { signedIn, laedt } = useHandelnder();
  const [form, setForm] = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const navigate = useNavigate();

  const load = useCallback(
    (cursor: string | undefined, signal: AbortSignal) =>
      searchJobs(applied, cursor, signal).then((result) =>
        result.ok ? result : ({ ok: false, fehler: result } as const),
      ),
    [applied],
  );
  const list = useSeiten<Job, SucheFehlschlag>(load, JSON.stringify(applied));

  // Einmal für die ganze Seite, nicht je Stelle. Solange die Sitzung unbekannt
  // ist, wird NICHT gefragt — sonst liefe beim Kaltstart ein Abruf, dessen
  // Antwort feststeht (401), und die Passung flackerte kurz auf.
  const profile = useAsync(
    (signal) => getMyProfile(signal),
    [signedIn],
    signedIn,
  );

  /*
   * Drei Zustände, und die Unterscheidung trägt:
   *   `null`     — nichts zu vergleichen: nicht angemeldet, oder die Antwort
   *                steht noch aus. Dann schweigt die Seite dazu, statt eine
   *                Lücke zu behaupten, die sie nicht kennt.
   *   `[]`       — angemeldet, Antwort da, aber nichts eingetragen. Darüber MUSS
   *                die Seite sprechen, sonst bliebe sie stumm, wo ein Satz die
   *                ganze Funktion erklärt — und niemals als „0 von 3".
   *   eine Liste — abgleichen.
   */
  const meineSkills: string[] | null =
    !signedIn || profile.pending ? null : (profile.data?.skills ?? []);

  const firmen = useFirmenprofile(list.items.map((job) => job.tenant_id));

  return (
    <PageShell
      title={t("stellen.titel")}
      lead={t("stellen.lead")}
    >
      <Card sx={{ mb: 3 }}>
        <CardContent
          component="form"
          onSubmit={(event: React.FormEvent) => {
            event.preventDefault();
            setApplied(form);
          }}
          sx={{
            display: "grid",
            gap: 2,
            gridTemplateColumns: { xs: "1fr", md: "repeat(4, 1fr) auto" },
            alignItems: "end",
          }}
        >
          <TextField
            label={t("stellen.suchbegriff")}
            placeholder={t("stellen.suchbegriffBeispiel")}
            value={form.q}
            onChange={(event) => setForm({ ...form, q: event.target.value })}
          />
          <TextField
            label={t("stellen.ort")}
            value={form.location}
            onChange={(event) =>
              setForm({ ...form, location: event.target.value })
            }
          />
          {/* Native Auswahlfelder: MUIs Voreinstellung ist ein Listenfeld aus
              `div`s, das weder `selectOption` noch ein Screenreader als
              Auswahlfeld bedient. */}
          <TextField
            select
            label={t("stellen.arbeitsform")}
            value={form.remote}
            slotProps={{
              select: { native: true },
              inputLabel: { shrink: true },
            }}
            onChange={(event) =>
              setForm({
                ...form,
                remote: event.target.value as RemoteMode | "",
              })
            }
          >
            <option value="">{t("stellen.egal")}</option>
            {REMOTE_MODES.map((value) => (
              <option key={value} value={value}>
                {remoteLabel(value)}
              </option>
            ))}
          </TextField>
          {/* Diesen Filter gab es schon: `searchJobs` schickt `employment` seit
              immer mit, nur konnte niemand ihn setzen. Ein Wähler dafür ist kein
              neues Versprechen, sondern das Einlösen eines vorhandenen. */}
          <TextField
            select
            label={t("stellen.beschaeftigungsart")}
            value={form.employment}
            slotProps={{
              select: { native: true },
              inputLabel: { shrink: true },
            }}
            onChange={(event) =>
              setForm({
                ...form,
                employment: event.target.value as EmploymentType | "",
              })
            }
          >
            <option value="">{t("stellen.egal")}</option>
            {EMPLOYMENT_TYPES.map((value) => (
              <option key={value} value={value}>
                {employmentLabel(value)}
              </option>
            ))}
          </TextField>
          <Button type="submit" variant="contained">
            {t("stellen.suchen")}
          </Button>
        </CardContent>
      </Card>

      {/* Reihenfolge auf jeder Liste: lädt, dann Fehler, dann leer, dann Inhalt. */}
      {list.pending && list.items.length === 0 ? (
        <LoadingBlock label={t("stellen.wirdGesucht")} />
      ) : null}

      {list.fehler !== null ? <ErrorBlock error={list.fehler.error} /> : null}

      {!list.pending && list.fehler === null && list.items.length === 0 ? (
        <EmptyBlock
          title={t("stellen.leerTitel")}
          hint={t("stellen.leerHinweis")}
        />
      ) : null}

      {list.items.length > 0 ? (
        <Box
          component="ul"
          sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 2 }}
        >
          {list.items.map((job) => (
            <Box component="li" key={job.id}>
              <Card>
                <CardContent>
                  <Typography variant="h3" sx={{ mb: 0.5 }}>
                    {job.title}
                  </Typography>
                  <Hiring profile={firmen[job.tenant_id]} />
                  <Typography
                    variant="body2"
                    color="text.secondary"
                    sx={{ mb: 1.5 }}
                  >
                    {job.location !== ""
                      ? job.location
                      : t("stellen.ortFehlt")}{" "}
                    · {remoteLabel(job.remote)} · {employmentLabel(job.employment)}
                  </Typography>
                  <Typography sx={{ whiteSpace: "pre-line" }}>
                    {job.description}
                  </Typography>
                  <Requirements skills={job.skills} mine={meineSkills} />

                  {laedt ? null : signedIn ? (
                    /*
                      Ein LINK auf eine eigene Adresse, kein aufklappendes
                      Formular in der Karte. Das Formular überlebt damit ein
                      Neuladen, ist teilbar, und die gemerkte Absicht nach dem
                      Anmelden hat ein echtes Ziel statt einer Liste, die eine
                      Box aufklappt.
                    */
                    <Button
                      component={RouterLink}
                      to={`/jobs/${job.id}/apply`}
                      variant="contained"
                      sx={{ mt: 2 }}
                    >
                      {t("stellen.bewerben")}
                    </Button>
                  ) : (
                    <Box sx={{ mt: 2 }}>
                      {/*
                        Ein KNOPF, kein Wort in einem Satz. Vorher stand hier
                        „Zum Bewerben anmelden." und nur das letzte Wort war ein
                        Link — für ein Programm anklickbar, für einen Menschen
                        Fließtext. Wer bewerben will, sucht einen Knopf, und er
                        muss dasselbe Gewicht haben wie der für Angemeldete.
                      */}
                      <Button
                        type="button"
                        variant="contained"
                        onClick={() => {
                          // Erst merken, dann wechseln. Dieser Knopf ist die
                          // EINZIGE Stelle, an der die Absicht entsteht — wer
                          // über die Kopfzeile zur Anmeldung geht, hat keine
                          // geäußert, und dann darf ihn auch nichts irgendwohin
                          // zurückwerfen.
                          merkeStelle(job.id, job.title);
                          void navigate("/login");
                        }}
                      >
                        {t("stellen.bewerben")}
                      </Button>
                      <Typography
                        variant="body2"
                        color="text.secondary"
                        sx={{ mt: 1 }}
                      >
                        {t("stellen.kontoNoetig")}
                      </Typography>
                    </Box>
                  )}
                </CardContent>
              </Card>
            </Box>
          ))}
        </Box>
      ) : null}

      {list.mehr ? (
        <Button onClick={list.weiter} disabled={list.pending} sx={{ mt: 2 }}>
          {list.pending ? t("allgemein.laden") : t("stellen.mehrLaden")}
        </Button>
      ) : null}
    </PageShell>
  );
}

/**
 * Wer sucht.
 *
 * Ohne Profil zeigt die Karte hier nichts — eine Stelle bleibt dann anonym.
 * Das ist ein Zustand, den das Unternehmen selbst herbeigeführt hat, und ihn mit
 * einem Platzhalter wie „Unbekanntes Unternehmen" zu füllen wäre eine Aussage,
 * die niemand gemacht hat.
 */
function Hiring({ profile }: { profile: CompanyProfile | null | undefined }) {
  const { t } = useTranslation();
  if (profile === undefined || profile === null) return null;

  return (
    <Typography variant="body2" sx={{ mb: 0.5 }}>
      <Box component="strong">{profile.display_name}</Box>
      {profile.website !== null ? (
        <>
          {" · "}
          <Link href={profile.website} target="_blank" rel="noreferrer noopener">
            {t("stellen.website")}
          </Link>
        </>
      ) : null}
      {profile.benefits.length > 0 ? <> · {profile.benefits.join(", ")}</> : null}
    </Typography>
  );
}
