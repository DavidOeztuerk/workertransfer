import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";
import { useLocation, useNavigate } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { AnmeldungNoetig } from "../../person/components/AnmeldungNoetig";
import { Schreibfortschritt } from "../components/Schreibfortschritt";
import { getJob } from "../api/jobs";
import { merke } from "../lib/kontext";
import { schreibbeginn } from "../lib/schreibuhr";
import { schreibschluessel } from "../lib/schreibfehler";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type Draft,
  type DraftStatus,
  deleteDraft,
  getDrafts,
  writeDraft,
} from "../api/applications";

const STATUS_FARBE: Record<
  DraftStatus,
  "default" | "primary" | "warning" | "info" | "error"
> = {
  generating: "warning",
  review: "info",
  needs_changes: "warning",
  approved: "primary",
  sent: "default",
  failed: "error",
};

export function DraftsPage() {
  const { t } = useTranslation();
  const { signedIn, laedt: sitzungLaedt } = useHandelnder();
  const location = useLocation();
  const navigate = useNavigate();
  const schreiben =
    (location.state as { schreiben?: "ok" | "keinAnbieter" } | null)?.schreiben ?? null;
  const [fehler, setFehler] = useState<string | null>(null);
  const [erfolg, setErfolg] = useState<string | null>(null);
  const [titel, setTitel] = useState<Record<string, string>>({});
  const [laeuft, setLaeuft] = useState<string | null>(null);

  function raeumeMeldungen() {
    setFehler(null);
    setErfolg(null);
    if (schreiben !== null) {
      navigate(".", { replace: true, state: {} });
    }
  }

  const entwuerfe = useAsync((signal) => getDrafts(signal), [], signedIn);
  const list = entwuerfe.data?.ok ? entwuerfe.data.drafts : [];
  const stellenIds = list.map((d) => d.job_id).join(",");
  const schreibtEiner = list.some((d) => d.status === "generating");

  useEffect(() => {
    if (!schreibtEiner) return;
    const timer = window.setInterval(() => entwuerfe.poll(), 2000);
    return () => window.clearInterval(timer);
  }, [schreibtEiner]);

  useEffect(() => {
    if (!signedIn || stellenIds === "") return;
    const ids = [...new Set(stellenIds.split(","))];
    let weg = false;
    void Promise.all(ids.map((id) => merke(`job:${id}`, () => getJob(id)))).then((jobs) => {
      if (weg) return;
      const next: Record<string, string> = {};
      ids.forEach((id, i) => {
        if (jobs[i]) next[id] = jobs[i]!.title;
      });
      setTitel(next);
    });
    return () => {
      weg = true;
    };
  }, [signedIn, stellenIds]);

  if (sitzungLaedt) {
    return (
      <PageShell title={t("entwurfs.titel")} narrow>
        <LoadingBlock label={t("entwurfs.laden")} />
      </PageShell>
    );
  }

  if (!signedIn) {
    return <AnmeldungNoetig titel={t("entwurfs.titel")} satz="entwurfs.anmelden" />;
  }

  async function nochmal(entwurf: Draft) {
    raeumeMeldungen();
    const result = await writeDraft(entwurf.id);
    if (!result.ok) {
      setFehler(t(schreibschluessel(result.error.detail)));
      return;
    }
    navigate(`/applications/drafts/${entwurf.id}`);
  }

  async function loeschen(id: string) {
    raeumeMeldungen();
    setLaeuft(id);
    const result = await deleteDraft(id);
    setLaeuft(null);
    if (!result.ok) setFehler(result.error.detail);
    entwuerfe.reload();
  }

  return (
    <PageShell title={t("entwurfs.titel")} lead={t("entwurfs.lead")}>
      {schreiben === "keinAnbieter" && laeuft === null ? (
        <Alert
          severity="info"
          sx={{ mb: 2 }}
          onClose={() => navigate(".", { replace: true, state: {} })}
        >
          {t("entwurfs.schreibenNichtGelungen")}
        </Alert>
      ) : null}
      {fehler && laeuft === null ? (
        <Alert severity="info" sx={{ mb: 2 }} onClose={() => setFehler(null)}>
          {fehler}
        </Alert>
      ) : null}
      {erfolg && laeuft === null ? (
        <Alert severity="info" sx={{ mb: 2 }} onClose={() => setErfolg(null)}>
          {erfolg}
        </Alert>
      ) : null}

      {entwuerfe.pending && list.length === 0 ? (
        <LoadingBlock label={t("entwurfs.laden")} />
      ) : null}
      {entwuerfe.data !== null && !entwuerfe.data.ok ? (
        <Alert severity="error">{entwuerfe.data.error.detail}</Alert>
      ) : null}
      {entwuerfe.data?.ok && list.length === 0 ? (
        <EmptyBlock
          title={t("entwurfs.leerTitel")}
          hint={t("entwurfs.leerHinweis")}
          action={
            <Button onClick={() => navigate("/jobs")} variant="contained">
              {t("bewerbungen.leerKnopf")}
            </Button>
          }
        />
      ) : null}
      {list.length > 0 ? (
        <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 2 }}>
          {list.map((entwurf) => {
            const stand: DraftStatus = entwurf.status;
            const offen = entwurf.comments.filter((c) => !c.resolved).length;
            const hinweis =
              stand === "failed" ? t(schreibschluessel(entwurf.error)) : null;
            return (
              <Card key={entwurf.id} component="li" variant="outlined">
                <CardContent
                  sx={{
                    display: "flex",
                    flexDirection: { xs: "column", sm: "row" },
                    justifyContent: "space-between",
                    gap: 2,
                  }}
                >
                  <Box sx={{ minWidth: 0, flex: 1 }}>
                    <Typography variant="h4">
                      {titel[entwurf.job_id] ?? t("entwurfs.stelleUnbekannt")}
                    </Typography>
                    <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap", mt: 0.5, mb: 1 }}>
                      <Chip
                        size="small"
                        label={t(`entwurfs.status.${stand}`)}
                        color={STATUS_FARBE[stand]}
                      />
                      <Typography variant="body2" color="text.secondary">
                        {t("entwurfs.fassung", { version: entwurf.version })}
                      </Typography>
                      {offen > 0 ? (
                        <Typography variant="body2" color="text.secondary">
                          {t("entwurfs.anmerkungen", { count: offen })}
                        </Typography>
                      ) : null}
                    </Box>
                    {stand === "review" ? (
                      <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                        {t("entwurfs.schreibenFertig")}
                      </Typography>
                    ) : stand === "generating" ? (
                      <Schreibfortschritt
                        seit={schreibbeginn(entwurf.id, entwurf.writing_started_at)}
                      />
                    ) : hinweis ? (
                      <Alert severity="info" sx={{ mt: 1.5 }}>
                        {hinweis}
                      </Alert>
                    ) : null}
                  </Box>
                  <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap", flexShrink: 0, alignItems: "flex-start" }}>
                    <Button
                      size="small"
                      variant="contained"
                      onClick={() => navigate(`/applications/drafts/${entwurf.id}`)}
                    >
                      {t("entwurfs.oeffnen")}
                    </Button>
                    {entwurf.status === "failed" ? (
                      <Button size="small" onClick={() => void nochmal(entwurf)}>
                        {t("entwurfs.nochmal")}
                      </Button>
                    ) : null}
                    {entwurf.status !== "sent" ? (
                      <Button
                        size="small"
                        color="error"
                        disabled={laeuft !== null}
                        onClick={() => void loeschen(entwurf.id)}
                      >
                        {t("allgemein.zurueckziehen")}
                      </Button>
                    ) : null}
                  </Box>
                </CardContent>
              </Card>
            );
          })}
        </Box>
      ) : null}
    </PageShell>
  );
}
