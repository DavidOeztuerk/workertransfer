import { useEffect, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Divider from "@mui/material/Divider";
import Link from "@mui/material/Link";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type MeinGespraech,
  beendeAlsPerson,
  gibFrei,
  ladeMandat,
  ladeMeineGespraeche,
  nimmZurueck,
  schreibeMandat,
  stimmeZu,
} from "../api/advisor";

/** Eine Zahl aus einem Textfeld, oder `null` für „nichts gesagt". */
function zahl(roh: string): number | null {
  const gekuerzt = roh.trim();
  if (gekuerzt.length === 0) return null;
  const wert = Number.parseInt(gekuerzt, 10);
  return Number.isNaN(wert) ? null : wert;
}

/** Eine Kommaliste in Domains, ohne Leeres. */
function domains(roh: string): string[] {
  return roh
    .split(",")
    .map((eintrag) => eintrag.trim().toLowerCase())
    .filter((eintrag) => eintrag.length > 0);
}

/**
 * <c>/advisor</c> — das eigene Mandat und die eigenen Gespräche.
 *
 * <strong>Hier steht kein Sichtbarkeitsschalter.</strong> Wer dich sehen darf,
 * entscheidest du auf der Profilseite und in jedem Gespräch einzeln; das Mandat
 * sind vier Angaben und sonst nichts (ADR-0037). Ein Schalter an dieser Stelle
 * wäre eine zweite Wahrheit neben dem Ledger — und die weicht beim ersten
 * Widerruf ab.
 *
 * <strong>Die Stufe steht an jedem Gespräch, und sie kommt vom Server.</strong>
 * Sie wird bei jeder Anfrage frisch aus dem Ledger gelesen. Deshalb lädt diese
 * Seite nach jeder Freigabe neu, statt die Zahl selbst weiterzuzählen: was
 * gerade gilt, weiss nur der Ledger.
 *
 * <strong>Und die Warnung am Ausschlussfeld ist keine Höflichkeit.</strong> Wer
 * ein Unternehmen ausschliesst, verliert „für alle Unternehmen sichtbar" — das
 * ist der Preis dafür, dass der Ledger keine Verneinung kennt, und die Person
 * muss ihn vorher lesen, nicht nachher merken.
 */
export function AdvisorPage() {
  const { t } = useTranslation();
  const { signedIn, laedt, subjectId } = useHandelnder();

  const mandat = useAsync((signal) => ladeMandat(signal), [subjectId], signedIn);
  const gespraeche = useAsync(
    (signal) => ladeMeineGespraeche(signal),
    [subjectId],
    signedIn,
  );

  const [eintritt, setEintritt] = useState("");
  const [von, setVon] = useState("");
  const [bis, setBis] = useState("");
  const [pensum, setPensum] = useState("");
  const [ausschluss, setAusschluss] = useState("");
  const [uebernommen, setUebernommen] = useState(false);
  const [laeuft, setLaeuft] = useState(false);
  const [gespeichert, setGespeichert] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);

  // Der geladene Stand fuellt das Formular GENAU EINMAL. Liefe das bei jeder
  // Antwort, ueberschriebe ein Neuladen die gerade getippte Zahl — und ein
  // Speichern schriebe sie zurueck, ohne dass jemand es merkt.
  useEffect(() => {
    if (uebernommen || !mandat.data?.ok) return;
    const stand = mandat.data.mandat;
    setEintritt(stand.entry_month ?? "");
    setVon(stand.salary_min === null ? "" : String(stand.salary_min));
    setBis(stand.salary_max === null ? "" : String(stand.salary_max));
    setPensum(stand.workload_percent === null ? "" : String(stand.workload_percent));
    setAusschluss(stand.excluded_domains.join(", "));
    setUebernommen(true);
  }, [uebernommen, mandat.data]);

  if (laedt) {
    return (
      <PageShell title={t("advisor.title")} narrow>
        <LoadingBlock label={t("advisor.loading")} />
      </PageShell>
    );
  }

  if (!signedIn) {
    return (
      <PageShell title={t("advisor.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="advisor.signedInOnly"
                components={{ 1: <Link component={RouterLink} to="/login" /> }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function speichern() {
    setLaeuft(true);
    const ergebnis = await schreibeMandat({
      entry_month: eintritt.trim().length === 0 ? null : eintritt.trim(),
      salary_min: zahl(von),
      salary_max: zahl(bis),
      workload_percent: zahl(pensum),
      excluded_domains: domains(ausschluss),
    });
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setGespeichert(true);
      mandat.reload();
      return;
    }

    setGespeichert(false);
    setFehler(ergebnis.error.detail ?? ergebnis.error.title);
  }

  async function stufenzug(id: string, hinauf: boolean, stufe: number) {
    setLaeuft(true);
    const ergebnis = hinauf ? await gibFrei(id, stufe) : await nimmZurueck(id, stufe);
    setLaeuft(false);

    setFehler(ergebnis.ok ? null : (ergebnis.error.detail ?? ergebnis.error.title));
    // IMMER neu laden, auch nach einem Fehlschlag: was jetzt gilt, weiss der
    // Ledger, und nicht diese Seite.
    gespraeche.reload();
  }

  async function zug(id: string, was: "agree" | "end") {
    setLaeuft(true);
    const ergebnis = was === "agree" ? await stimmeZu(id) : await beendeAlsPerson(id);
    setLaeuft(false);

    setFehler(ergebnis.ok ? null : (ergebnis.error.detail ?? ergebnis.error.title));
    gespraeche.reload();
  }

  return (
    <PageShell title={t("advisor.title")} narrow>
      <Typography sx={{ mb: 3 }}>{t("advisor.lead")}</Typography>

      {fehler !== null && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      )}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>
            {t("advisor.mandateTitle")}
          </Typography>
          <Typography variant="body2" sx={{ mb: 2 }}>
            {t("advisor.mandateLead")}
          </Typography>

          {mandat.pending && <LoadingBlock label={t("advisor.loading")} />}

          {!mandat.pending && mandat.data !== null && !mandat.data.ok && (
            <Alert
              // No substitute form when the mandate cannot be read: whoever
              // touches something and saves silently erases their own entries.
              severity="warning"
            >
              {t("advisor.mandateUnavailable")}
            </Alert>
          )}

          {mandat.data?.ok === true && (
            <Stack spacing={2}>
              <TextField
                label={t("advisor.earliestStart")}
                helperText={t("advisor.earliestStartHint")}
                value={eintritt}
                placeholder="2026-11"
                onChange={(event) => setEintritt(event.target.value)}
              />

              <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                <TextField
                  label={t("advisor.salaryFrom")}
                  value={von}
                  inputMode="numeric"
                  onChange={(event) => setVon(event.target.value)}
                  fullWidth
                />
                <TextField
                  label={t("advisor.salaryTo")}
                  value={bis}
                  inputMode="numeric"
                  onChange={(event) => setBis(event.target.value)}
                  fullWidth
                />
              </Stack>
              <Typography variant="caption">{t("advisor.salaryHint")}</Typography>

              <TextField
                label={t("advisor.workload")}
                helperText={t("advisor.workloadHint")}
                value={pensum}
                inputMode="numeric"
                onChange={(event) => setPensum(event.target.value)}
              />

              <TextField
                label={t("advisor.excluded")}
                helperText={t("advisor.excludedHint")}
                value={ausschluss}
                placeholder="arbeitgeber.de"
                onChange={(event) => setAusschluss(event.target.value)}
              />

              <Alert
                // Always visible and not only while typing: the price belongs
                // BEFORE the decision, not behind it.
                severity="info"
              >
                {t("advisor.excludedWarning")}
              </Alert>

              {gespeichert && <Alert severity="success">{t("advisor.saved")}</Alert>}

              <Box>
                <Button variant="contained" onClick={speichern} disabled={laeuft}>
                  {laeuft ? t("advisor.saving") : t("advisor.save")}
                </Button>
              </Box>
            </Stack>
          )}
        </CardContent>
      </Card>

      <Typography variant="h2" sx={{ mb: 1 }}>
        {t("advisor.conversationsTitle")}
      </Typography>
      <Typography variant="body2" sx={{ mb: 2 }}>
        {t("advisor.conversationsLead")}
      </Typography>

      {gespraeche.pending && <LoadingBlock label={t("advisor.loading")} />}

      {gespraeche.data?.ok === false && (
        <Alert severity="warning">{t("errors.ledgerSilent")}</Alert>
      )}

      {gespraeche.data?.ok === true && gespraeche.data.items.length === 0 && (
        <EmptyBlock
          title={t("advisor.noConversations")}
          hint={t("advisor.noConversationsHint")}
        />
      )}

      <Stack spacing={2}>
        {gespraeche.data?.ok === true &&
          gespraeche.data.items.map((gespraech) => (
            <Gespraechskarte
              key={gespraech.id}
              gespraech={gespraech}
              laeuft={laeuft}
              aufStufe={(hinauf, stufe) => stufenzug(gespraech.id, hinauf, stufe)}
              zug={(was) => zug(gespraech.id, was)}
            />
          ))}
      </Stack>
    </PageShell>
  );
}

/**
 * Eine Karte je Gespräch.
 *
 * <strong>Der nächste Schritt ist genau ein Knopf.</strong> Drei Knöpfe
 * nebeneinander liessen die Wahl aussehen wie eine Voreinstellung; eine Stufe
 * ist ein Stand, und man geht ihn hinauf.
 */
function Gespraechskarte({
  gespraech,
  laeuft,
  aufStufe,
  zug,
}: {
  gespraech: MeinGespraech;
  laeuft: boolean;
  aufStufe: (hinauf: boolean, stufe: number) => void;
  zug: (was: "agree" | "end") => void;
}) {
  const { t } = useTranslation();
  const naechste = gespraech.stage + 1;
  const offen = gespraech.state === "talking" || gespraech.state === "agreed";

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap" }}>
          <Chip label={t(`advisor.state_${gespraech.state}`)} size="small" />
          <Chip
            label={t(`advisor.stage${gespraech.stage}Short`)}
            size="small"
            color={gespraech.stage > 0 ? "primary" : "default"}
            variant={gespraech.stage > 0 ? "filled" : "outlined"}
          />
        </Stack>

        {gespraech.note.length > 0 && (
          <Typography sx={{ mb: 1 }}>{gespraech.note}</Typography>
        )}

        <Typography variant="caption" sx={{ display: "block", mb: 1 }}>
          {t("advisor.openedOn", {
            datum: new Date(gespraech.opened_at).toLocaleDateString(),
          })}
        </Typography>

        <Typography variant="body2" sx={{ mb: 2 }}>
          {t(`advisor.stage${Math.min(gespraech.stage + 1, 3)}`)}
        </Typography>

        <Divider sx={{ mb: 2 }} />

        <Typography variant="caption" sx={{ display: "block", mb: 1 }}>
          {t("advisor.effectImmediate")}
        </Typography>

        {gespraech.stage > 0 && (
          <Typography
            // "Withdraw everything" would promise too much: whoever set their
            // profile to "for all companies" stays visible through that, and the
            // stage does not fall to 0. That is right — a button in ONE
            // conversation must not flip a platform-wide switch — but it has to
            // be said here.
            variant="caption"
            sx={{ display: "block", mb: 1 }}
          >
            {t("advisor.backHint")}
          </Typography>
        )}

        <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
          {offen && naechste <= 3 && (
            <Button
              variant="contained"
              size="small"
              disabled={laeuft}
              onClick={() => aufStufe(true, naechste)}
            >
              {t("advisor.releaseStage", { nummer: naechste })}
            </Button>
          )}

          {gespraech.stage > 0 && (
            <Button
              size="small"
              disabled={laeuft}
              // ONE button and not three: a stage is a standing and you walk it
              // down. `withdraw` takes everything from the named stage upwards —
              // from 3 leaves 2, from 1 leaves 0.
              onClick={() => aufStufe(false, gespraech.stage)}
            >
              {gespraech.stage === 1
                ? t("advisor.withdrawRelease")
                : t("advisor.backToStage", { nummer: gespraech.stage - 1 })}
            </Button>
          )}

          {gespraech.state === "talking" && (
            <Button size="small" disabled={laeuft} onClick={() => zug("agree")}>
              {t("advisor.agree")}
            </Button>
          )}

          {offen && (
            <Button size="small" color="error" disabled={laeuft} onClick={() => zug("end")}>
              {t("advisor.end")}
            </Button>
          )}
        </Stack>

        {gespraech.state === "agreed" && (
          <Alert severity="success" sx={{ mt: 2 }}>
            {t("advisor.agreed")}
          </Alert>
        )}
      </CardContent>
    </Card>
  );
}

export default AdvisorPage;
