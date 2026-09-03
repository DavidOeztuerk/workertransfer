import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate } from "react-router-dom";

import {
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
  PaginationControls,
} from "../../../shared/components/ui";
import type { CompanyProfile } from "../../company/api/companies";
import {
  employmentLabel,
  type Job,
  remoteLabel,
  type SearchFilters,
  type SucheFehlschlag,
  searchJobs,
} from "../api/jobs";
import { getMyProfile } from "../api/profile";
import { Requirements } from "../components/Requirements";
import { merkeStelle } from "../lib/intent";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import { useFirmenprofile } from "../lib/useFirmenprofile";
import {
  type Seitenergebnis,
  useBlaettern,
} from "../../../shared/hooks/useBlaettern";
import { size } from "../../../styles/tokens/spacing";
import {
  JobFilterSidebar,
  LEERER_FILTER,
  type Stellenfilter,
} from "../components/JobFilterSidebar";

/**
 * Aus dem Formularzustand wird die Anfrage.
 *
 * Die Fähigkeiten stehen im Formular als EINE Zeichenkette, weil man sie so
 * tippt, und gehen als Liste hinaus, weil der Server sie so erwartet. Die
 * Trennung passiert genau hier und nirgends sonst.
 */
function zuFiltern(filter: Stellenfilter): SearchFilters {
  return {
    q: filter.q,
    location: filter.location,
    remote: filter.remote,
    employment: filter.employment,
    skills: filter.skills
      .split(",")
      .map((einzeln) => einzeln.trim())
      .filter((einzeln) => einzeln !== ""),
  };
}

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
  const [form, setForm] = useState<Stellenfilter>(LEERER_FILTER);
  const [applied, setApplied] = useState<Stellenfilter>(LEERER_FILTER);
  const navigate = useNavigate();

  const load = useCallback(
    (page: number, pageSize: number, signal: AbortSignal) =>
      searchJobs(zuFiltern(applied), page, pageSize, signal).then(
        (result): Seitenergebnis<Job, SucheFehlschlag> =>
          result.ok
            ? { ok: true, seite: result }
            : { ok: false, fehler: result },
      ),
    [applied],
  );

  const list = useBlaettern<Job, SucheFehlschlag>(load, JSON.stringify(applied));

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
      <Box
        sx={{
          display: "grid",
          gap: { xs: 3, md: 4 },
          gridTemplateColumns: { xs: "1fr", md: `${size.filterSidebar}px 1fr` },
          alignItems: "start",
        }}
      >
        <JobFilterSidebar
          entwurf={form}
          onChange={setForm}
          onSubmit={() => setApplied(form)}
          onReset={() => {
            setForm(LEERER_FILTER);
            setApplied(LEERER_FILTER);
          }}
        />

        <Box>
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
          sx={{
            listStyle: "none",
            p: 0,
            m: 0,
            display: "grid",
            gap: 2,
            // Zwei Spalten ab `lg`. Zwölf Karten untereinander sind zwölf
            // Bildschirmhöhen; nebeneinander ist es eine Liste, die man
            // überblickt.
            gridTemplateColumns: { xs: "1fr", lg: "repeat(2, 1fr)" },
          }}
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
                    /*
                      Derselbe Knopf wie für Angemeldete, und OHNE den Satz
                      „dafür brauchst du ein Konto". Er stand unter jeder Karte
                      und erklärte etwas, das im nächsten Augenblick ohnehin auf
                      dem Bildschirm steht: wer klickt, landet in der Anmeldung
                      und sieht dort, warum. Zwölf Karten mit zwölf Hinweisen auf
                      dieselbe Sache sind Lärm.

                      Ein KNOPF und kein Wort in einem Satz: wer bewerben will,
                      sucht einen Knopf, und er muss dasselbe Gewicht haben wie
                      der für Angemeldete.
                    */
                    <Button
                      type="button"
                      variant="contained"
                      sx={{ mt: 2 }}
                      onClick={() => {
                        // Erst merken, dann wechseln. Dieser Knopf ist die
                        // EINZIGE Stelle, an der die Absicht entsteht — wer über
                        // die Kopfzeile zur Anmeldung geht, hat keine geäussert,
                        // und dann darf ihn auch nichts irgendwohin zurückwerfen.
                        merkeStelle(job.id, job.title);
                        void navigate("/login");
                      }}
                    >
                      {t("stellen.bewerben")}
                    </Button>
                  )}
                </CardContent>
              </Card>
            </Box>
          ))}
        </Box>
      ) : null}

          <PaginationControls
            page={list.page}
            pageSize={list.pageSize}
            totalItems={list.totalItems}
            totalPages={list.totalPages}
            onPage={list.gehe}
            onPageSize={list.setzeGroesse}
          />
        </Box>
      </Box>
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
