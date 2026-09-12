import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import CircularProgress from "@mui/material/CircularProgress";
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
  ortZuPunkt,
  searchJobs,
} from "../api/jobs";
import { getDrafts, listMyApplications } from "../api/applications";
import { getMyProfile } from "../api/profile";
import { merke } from "../lib/kontext";
import { Requirements } from "../components/Requirements";
import { type Fortschrittsmelder, starteEntwuerfe } from "../lib/entwuerfe";
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
import { useGeolocation } from "../../../shared/hooks/useGeolocation";

/**
 * Was gesucht wurde: die Filter des Formulars UND der Punkt, der beim Absenden
 * bekannt war.
 *
 * Der Standort steht getrennt, weil er kein Formularfeld ist: er kommt vom
 * Browser, nicht aus einer Eingabe. Er gehört aber in denselben Zustand wie die
 * Filter, damit ein Wechsel des Standorts dieselbe Wirkung hat wie ein
 * Filterwechsel — nämlich eine neue Suche ab Seite eins.
 */
interface Angewandt extends Stellenfilter {
  lat: number | null;
  lon: number | null;
}

const NICHTS_GESUCHT: Angewandt = { ...LEERER_FILTER, lat: null, lon: null };

/**
 * Aus dem Formularzustand wird die Anfrage.
 *
 * Die Fähigkeiten stehen im Formular als EINE Zeichenkette, weil man sie so
 * tippt, und gehen als Liste hinaus, weil der Server sie so erwartet. Die
 * Trennung passiert genau hier und nirgends sonst.
 */
function zuFiltern(filter: Angewandt): SearchFilters {
  const radiusKm = Number(filter.radius);

  /*
   * Der Radius geht mit, sobald einer gewählt wurde. Die Koordinaten nur, wenn
   * es welche gibt — sonst ist der ORT die Mitte, und der Dienst löst ihn auf.
   *
   * Die Oberfläche lässt eine Entfernung ohnehin erst zu, wenn eine Mitte da
   * ist: ein Standort oder ein getippter Ort.
   */
  const umkreis =
    Number.isFinite(radiusKm) && radiusKm > 0
      ? {
          radiusKm,
          ...(filter.lat !== null && filter.lon !== null
            ? { lat: filter.lat, lon: filter.lon }
            : {}),
        }
      : {};

  return {
    q: filter.q,
    location: filter.location,
    remote: filter.remote,
    employment: filter.employment,
    skills: filter.skills
      .split(",")
      .map((einzeln) => einzeln.trim())
      .filter((einzeln) => einzeln !== ""),
    ...umkreis,
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
  // OHNE Vorspann. Hier stand „Was hier steht, haben Unternehmen selbst
  // veröffentlicht. Zum Lesen brauchst du kein Konto — erst zum Bewerben."
  // Beide Sätze erklären, was die Seite selbst schon zeigt: dass sie ohne
  // Anmeldung dasteht, und dass unter jeder Karte ein Knopf „Bewerben" sitzt.
  // Zwei Zeilen, die den ersten Treffer nach unten schieben.
  const { t } = useTranslation();
  const { signedIn, laedt } = useHandelnder();
  const meineBewerbungen = useAsync(
    (signal) => listMyApplications(signal),
    [],
    signedIn,
  );
  const meineEntwuerfe = useAsync((signal) => getDrafts(signal), [], signedIn);
  const schonGesendet = new Set(
    (meineBewerbungen.data?.ok ? meineBewerbungen.data.applications : [])
      .filter((a) => a.status !== "withdrawn")
      .map((a) => a.job_id),
  );
  const offeneEntwuerfe = new Map(
    (meineEntwuerfe.data?.ok ? meineEntwuerfe.data.drafts : [])
      .filter((d) => d.status !== "sent")
      .map((d) => [d.job_id, d.id]),
  );
  const [form, setForm] = useState<Stellenfilter>(LEERER_FILTER);
  const [applied, setApplied] = useState<Angewandt>(NICHTS_GESUCHT);
  const [selectedJobs, setSelectedJobs] = useState<Set<string>>(new Set());
  const [creatingDrafts, setCreatingDrafts] = useState(false);
  const [laeuftJob, setLaeuftJob] = useState<string | null>(null);
  /*
   * Wie weit das Schreiben ist — `null`, solange nichts laeuft.
   *
   * Er wird nur beim Sammelweg gesetzt: dort dauert es lange genug, dass
   * jemand wissen will, ob noch etwas passiert. Fuer eine einzelne Stelle
   * genuegt der Kreisel im Knopf, und die Leiste unten steht ohnehin nur bei
   * einer Auswahl da.
   */
  const [fortschritt, setFortschritt] = useState<{ fertig: number; gesamt: number } | null>(
    null,
  );
  const [bulkFehler, setBulkFehler] = useState<string | null>(null);
  const navigate = useNavigate();

  // Der Standort wird nie beim Laden erfragt — nur auf Knopfdruck in der
  // Seitenleiste, und er wird nirgends gespeichert. Siehe `useGeolocation`.
  const ortung = useGeolocation();

  /*
   * DER GEFUNDENE STANDORT WIRD SICHTBAR — er landet im Ortsfeld.
   *
   * Ein Umkreis um einen unsichtbaren Punkt ist eine Zumutung: die Liste würde
   * sich ändern, und niemand könnte sagen, wovon aus gemessen wurde. Der Name
   * kommt aus derselben Tabelle wie die Suche selbst, nicht von einem
   * Fremdanbieter.
   *
   * Der Merker verhindert die Schleife: das Setzt ändert das Formular, das
   * Formular ist aber keine Abhängigkeit dieses Effekts — geholt wird je
   * Position genau einmal.
   */
  const geholtFuer = useRef<string | null>(null);

  useEffect(() => {
    const standort = ortung.standort;

    if (standort === null) {
      geholtFuer.current = null;
      return;
    }

    const schluessel = `${standort.lat},${standort.lon}`;

    if (geholtFuer.current === schluessel) return;
    geholtFuer.current = schluessel;

    const abbruch = new AbortController();

    void ortZuPunkt(standort.lat, standort.lon, abbruch.signal).then((name) => {
      // `null` heisst „nichts in der Nähe" — dann bleibt das Feld, wie es war,
      // statt einen Ort zu behaupten.
      if (name !== null) {
        setForm((vorher) => ({ ...vorher, location: name }));
      }
    });

    return () => abbruch.abort();
  }, [ortung.standort]);

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
    () => merke("profile:me", () => getMyProfile()),
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

  const toggleJob = useCallback((jobId: string) => {
    setSelectedJobs((prev) => {
      const next = new Set(prev);
      if (next.has(jobId)) {
        next.delete(jobId);
      } else {
        next.add(jobId);
      }
      return next;
    });
  }, []);

  async function anlegen(jobIds: string[], melde?: Fortschrittsmelder) {
    const start = await starteEntwuerfe(jobIds, melde);
    if (!start.ok) {
      setBulkFehler(start.fehler);
      return null;
    }
    return start.drafts;
  }

  async function bewerbenAufEine(jobId: string) {
    setLaeuftJob(jobId);
    setBulkFehler(null);
    try {
      const drafts = await anlegen([jobId]);
      const erster = drafts?.[0];
      if (erster) navigate(`/applications/drafts/${erster.id}`);
    } finally {
      setLaeuftJob(null);
    }
  }

  const clearSelection = useCallback(() => {
    setSelectedJobs(new Set());
  }, []);

  const handleBulkApply = useCallback(async () => {
    if (!signedIn || selectedJobs.size === 0) return;

    setCreatingDrafts(true);
    setBulkFehler(null);
    try {
      const jobIds = Array.from(selectedJobs).filter((id) => !offeneEntwuerfe.has(id));
      if (jobIds.length === 0) {
        clearSelection();
        return;
      }
      const drafts = await anlegen(jobIds, (fertig, gesamt) =>
        setFortschritt({ fertig, gesamt }),
      );
      if (drafts === null) return;

      clearSelection();
      navigate("/applications/drafts", { state: { schreiben: "ok" } });
    } finally {
      setCreatingDrafts(false);
      setFortschritt(null);
    }
  }, [signedIn, selectedJobs, navigate, clearSelection]);

  return (
    <PageShell title={t("stellen.titel")}>
      {bulkFehler ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {bulkFehler}
        </Alert>
      ) : null}
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
          ortung={ortung}
          onSubmit={() =>
            setApplied({
              ...form,
              lat: ortung.standort?.lat ?? null,
              lon: ortung.standort?.lon ?? null,
            })
          }
          onReset={() => {
            setForm(LEERER_FILTER);
            setApplied(NICHTS_GESUCHT);
            ortung.vergiss();
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

      {/*
        WAS DER FILTER NICHT BEURTEILEN KONNTE, steht über der Liste und nicht
        klein darunter. Eine Umkreissuche kann über eine Anzeige, deren Ort die
        Ortstabelle nicht kennt, nichts sagen — sie stumm wegzulassen ergäbe ein
        Ergebnis, das vollständig aussieht und es nicht ist (ADR-0022 §3).
      */}
      {list.omitted !== null && list.omitted > 0 ? (
        <Alert severity="info" icon={false} sx={{ mb: 2 }}>
          {t("stellen.ohneOrt", { count: list.omitted })}
        </Alert>
      ) : null}

      {list.items.length > 0 ? (
        <>
          <Box
            component="ul"
            sx={{
              listStyle: "none",
              p: 0,
              m: 0,
              display: "grid",
              gap: 2,
              pb: signedIn && selectedJobs.size > 0 ? 10 : 0,
              // EINE Spalte, auf jeder Breite. Zwei nebeneinander waren kürzer
              // und schlechter: Stellen sind verschieden lang, also entstanden
              // ungleiche Karten mit Löchern dazwischen, und der Blick musste
              // nach jeder Karte zurück nach links springen. Eine Liste liest man
              // von oben nach unten.
            }}
          >
            {list.items.map((job) => (
              <Box component="li" key={job.id}>
                <Card>
                  <CardContent>
                    <Box sx={{ display: "flex", alignItems: "flex-start", gap: 2, mb: 1 }}>
                      {!laedt && signedIn && !schonGesendet.has(job.id) && (
                        <Checkbox
                          checked={selectedJobs.has(job.id)}
                          disabled={offeneEntwuerfe.has(job.id) || creatingDrafts || laeuftJob !== null}
                          onChange={() => toggleJob(job.id)}
                          slotProps={{ input: { "aria-label": t("stellen.auswaehlen") } }}
                          sx={{ mt: 0.5 }}
                        />
                      )}
                      <Box sx={{ flex: 1 }}>
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
                      </Box>
                    </Box>

                    {laedt ? null : signedIn ? (
                      schonGesendet.has(job.id) ? (
                        <Button variant="contained" disabled sx={{ mt: 2 }}>
                          {t("stellen.bereitsBeworben")}
                        </Button>
                      ) : offeneEntwuerfe.has(job.id) ? (
                        <Button
                          component={RouterLink}
                          to={`/applications/drafts/${offeneEntwuerfe.get(job.id)}`}
                          variant="contained"
                          sx={{ mt: 2 }}
                        >
                          {t("stellen.entwurfOeffnen")}
                        </Button>
                      ) : (
                      <Button
                        variant="contained"
                        sx={{ mt: 2 }}
                        disabled={creatingDrafts || laeuftJob !== null}
                        onClick={() => void bewerbenAufEine(job.id)}
                        startIcon={
                          laeuftJob === job.id ? (
                            <CircularProgress color="inherit" size={16} />
                          ) : null
                        }
                      >
                        {t("stellen.bewerben")}
                      </Button>
                      )
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
                        onClick={() => void navigate("/login")}
                      >
                        {t("stellen.bewerben")}
                      </Button>
                    )}
                  </CardContent>
                </Card>
              </Box>
            ))}
          </Box>

          {/* Multi-Select Leiste */}
          {signedIn && selectedJobs.size > 0 && (
            <Box
              sx={{
                position: "fixed",
                bottom: 0,
                left: 0,
                right: 0,
                p: 2,
                bgcolor: "background.paper",
                borderTop: "1px solid",
                borderColor: "divider",
                boxShadow: 3,
                zIndex: 1100,
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                flexWrap: "wrap",
                gap: 2,
              }}
            >
              <Typography variant="body1">
                {fortschritt
                  ? t("stellen.fortschritt", {
                      fertig: fortschritt.fertig,
                      gesamt: fortschritt.gesamt,
                    })
                  : t("stellen.ausgewaehlt", { count: selectedJobs.size })}
              </Typography>
              <Box sx={{ display: "flex", gap: 1 }}>
                <Button
                  variant="outlined"
                  onClick={clearSelection}
                  disabled={creatingDrafts}
                >
                  {t("stellen.auswahlAufheben")}
                </Button>
                <Button
                  variant="contained"
                  onClick={handleBulkApply}
                  disabled={creatingDrafts}
                  startIcon={creatingDrafts ? <Box sx={{ width: 16, height: 16 }} /> : null}
                >
                  {creatingDrafts
                    ? t("stellen.entwuerfeErstellen")
                    : t("stellen.fuerAlleBewerben")}
                </Button>
              </Box>
            </Box>
          )}
        </>
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
