import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Divider from "@mui/material/Divider";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../../work/lib/session";
import { useAsync } from "../../work/lib/useAsync";
import {
  type Gespraech,
  beendeAlsFirma,
  ladeFirmengespraeche,
  uebergib,
} from "../../work/api/advisor";

/**
 * <c>/company/advisor</c> — die Gespräche eines Unternehmens.
 *
 * <strong>Was hier fehlt, fehlt.</strong> Die Felder der höheren Stufen sind in
 * der Antwort nicht enthalten, wenn sie nicht freigegeben sind — nicht `null`,
 * sondern gar nicht da. Diese Seite darf daraus keinen Hinweis bauen: kein
 * „noch nicht freigegeben", kein Schloss, keine graue Zeile. Ein solcher Hinweis
 * verriete genau das, was die Stufe zurückhält — dass es etwas gibt (ADR-0037).
 *
 * <strong>Und ein Gespräch auf Stufe 0 steht gar nicht in dieser Liste.</strong>
 * Der Server lässt es weg; eine leere Zeile wäre derselbe Hinweis in
 * Listenform.
 */
export function CompanyAdvisorPage() {
  const { t } = useTranslation();
  const { signedIn, laedt, fuerFirma, tenantId } = useHandelnder();

  const gespraeche = useAsync(
    (signal) => ladeFirmengespraeche(signal),
    [tenantId],
    signedIn && fuerFirma,
  );

  const [laeuft, setLaeuft] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [fertig, setFertig] = useState(false);

  if (laedt) {
    return (
      <PageShell title={t("berater.firmaTitel")} narrow>
        <LoadingBlock label={t("berater.laden")} />
      </PageShell>
    );
  }

  if (!signedIn || !fuerFirma) {
    return (
      <PageShell title={t("berater.firmaTitel")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("berater.firmaNurFirma")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function zug(id: string, was: "hand-over" | "end") {
    setLaeuft(true);
    const ergebnis = was === "hand-over" ? await uebergib(id) : await beendeAlsFirma(id);
    setLaeuft(false);

    setFehler(ergebnis.ok ? null : (ergebnis.error.detail ?? ergebnis.error.title));
    setFertig(ergebnis.ok && was === "hand-over");
    gespraeche.reload();
  }

  return (
    <PageShell title={t("berater.firmaTitel")} narrow>
      <Typography sx={{ mb: 3 }}>{t("berater.firmaLead")}</Typography>

      {fehler !== null && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      )}

      {fertig && (
        <Alert severity="success" sx={{ mb: 2 }}>
          {t("berater.uebergebenFertig")}
        </Alert>
      )}

      {gespraeche.pending && <LoadingBlock label={t("berater.laden")} />}

      {gespraeche.data?.ok === false && (
        <Alert severity="warning">
          {gespraeche.data.reason === "no-company"
            ? t("berater.firmaNurFirma")
            : t("fehler.ledgerSchweigt")}
        </Alert>
      )}

      {gespraeche.data?.ok === true && gespraeche.data.items.length === 0 && (
        <EmptyBlock title={t("berater.firmaLeer")} hint={t("berater.firmaLeerHinweis")} />
      )}

      <Stack spacing={2}>
        {gespraeche.data?.ok === true &&
          gespraeche.data.items.map((gespraech) => (
            <Firmenkarte
              key={gespraech.id}
              gespraech={gespraech}
              laeuft={laeuft}
              zug={(was) => zug(gespraech.id, was)}
            />
          ))}
      </Stack>
    </PageShell>
  );
}

/** Eine Karte je Gespräch — mit genau den Feldern, die angekommen sind. */
function Firmenkarte({
  gespraech,
  laeuft,
  zug,
}: {
  gespraech: Gespraech;
  laeuft: boolean;
  zug: (was: "hand-over" | "end") => void;
}) {
  const { t } = useTranslation();
  const offen = gespraech.state === "talking" || gespraech.state === "agreed";

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap" }}>
          <Chip label={t(`berater.stand_${gespraech.state}`)} size="small" />
          <Chip
            label={t("berater.firmaStufe", {
              stand: t(`berater.stufe${gespraech.stage}Kurz`),
            })}
            size="small"
            variant="outlined"
          />
        </Stack>

        {/* Ab Stufe 3. Fehlt der Name, steht hier die Subjekt-Kennung — und
            ausdruecklich kein Platzhalter wie „anonym": der behauptete, die
            Person habe sich verborgen, und das waere eine Aussage ueber sie. */}
        <Typography variant="h3" sx={{ mb: 1 }}>
          {gespraech.name ?? gespraech.subject_id}
        </Typography>

        {gespraech.email !== undefined && (
          <Typography variant="body2" sx={{ mb: 1 }}>
            {gespraech.email}
          </Typography>
        )}

        {gespraech.note.length > 0 && (
          <Typography sx={{ mb: 1 }}>{gespraech.note}</Typography>
        )}

        <Divider sx={{ my: 2 }} />

        <Stack spacing={0.5} sx={{ mb: 2 }}>
          {/* Ab Stufe 1. Fehlt das Feld, steht hier NICHTS — kein „nicht
              angegeben", kein „noch nicht freigegeben". Die beiden waeren von
              aussen nicht zu unterscheiden, und genau das ist die Zusage. */}
          {gespraech.entry_month !== undefined && (
            <Typography variant="body2">
              {t("berater.eintritt")}: {gespraech.entry_month}
            </Typography>
          )}
          {gespraech.workload_percent !== undefined && (
            <Typography variant="body2">
              {t("berater.pensum")}: {gespraech.workload_percent}
            </Typography>
          )}
          {/* Ab Stufe 2. */}
          {(gespraech.salary_min !== undefined || gespraech.salary_max !== undefined) && (
            <Typography variant="body2">
              {gespraech.salary_min !== undefined && gespraech.salary_max !== undefined
                ? t("berater.spanne", {
                    von: gespraech.salary_min,
                    bis: gespraech.salary_max,
                  })
                : gespraech.salary_min !== undefined
                  ? t("berater.bisOffen", { von: gespraech.salary_min })
                  : t("berater.vonOffen", { bis: gespraech.salary_max })}
            </Typography>
          )}
        </Stack>

        <Typography variant="caption" sx={{ display: "block", mb: 1 }}>
          {t("berater.uebergebenHinweis")}
        </Typography>

        <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
          {gespraech.state === "agreed" && (
            <Button
              variant="contained"
              size="small"
              disabled={laeuft}
              onClick={() => zug("hand-over")}
            >
              {t("berater.uebergeben")}
            </Button>
          )}

          {offen && (
            <Button size="small" color="error" disabled={laeuft} onClick={() => zug("end")}>
              {t("berater.beenden")}
            </Button>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}

export default CompanyAdvisorPage;
