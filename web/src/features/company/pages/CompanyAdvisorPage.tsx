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
import { useActor } from "../../work/lib/session";
import { useAsync } from "../../work/lib/useAsync";
import {
  type Conversation,
  endAsCompany,
  getCompanyConversations,
  handOver,
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
  const { signedIn, loading: laedt, forCompany: fuerFirma, tenantId } = useActor();

  const gespraeche = useAsync(
    (signal) => getCompanyConversations(signal),
    [tenantId],
    signedIn && fuerFirma,
  );

  const [laeuft, setLaeuft] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [fertig, setFertig] = useState(false);

  if (laedt) {
    return (
      <PageShell title={t("advisor.companyTitle")} narrow>
        <LoadingBlock label={t("advisor.loading")} />
      </PageShell>
    );
  }

  if (!signedIn || !fuerFirma) {
    return (
      <PageShell title={t("advisor.companyTitle")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("advisor.companyOnly")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function zug(id: string, was: "hand-over" | "end") {
    setLaeuft(true);
    const ergebnis = was === "hand-over" ? await handOver(id) : await endAsCompany(id);
    setLaeuft(false);

    setFehler(ergebnis.ok ? null : (ergebnis.error.detail ?? ergebnis.error.title));
    setFertig(ergebnis.ok && was === "hand-over");
    gespraeche.reload();
  }

  return (
    <PageShell title={t("advisor.companyTitle")} narrow>
      <Typography sx={{ mb: 3 }}>{t("advisor.companyLead")}</Typography>

      {fehler !== null && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      )}

      {fertig && (
        <Alert severity="success" sx={{ mb: 2 }}>
          {t("advisor.handOverDone")}
        </Alert>
      )}

      {gespraeche.pending && <LoadingBlock label={t("advisor.loading")} />}

      {gespraeche.data?.ok === false && (
        <Alert severity="warning">
          {gespraeche.data.reason === "no-company"
            ? t("advisor.companyOnly")
            : t("errors.ledgerSilent")}
        </Alert>
      )}

      {gespraeche.data?.ok === true && gespraeche.data.items.length === 0 && (
        <EmptyBlock title={t("advisor.companyEmpty")} hint={t("advisor.companyEmptyHint")} />
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
  gespraech: Conversation;
  laeuft: boolean;
  zug: (was: "hand-over" | "end") => void;
}) {
  const { t } = useTranslation();
  const offen = gespraech.state === "talking" || gespraech.state === "agreed";

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap" }}>
          <Chip label={t(`advisor.state_${gespraech.state}`)} size="small" />
          <Chip
            label={t("advisor.companyStage", {
              stand: t(`advisor.stage${gespraech.stage}Short`),
            })}
            size="small"
            variant="outlined"
          />
        </Stack>

        {gespraech.name !== undefined ? (
          <Typography variant="h3" sx={{ mb: 1 }}>
            {gespraech.name}
          </Typography>
        ) : (
          // Without a name the id is shown as what it is — a case reference,
          // not a heading. No placeholder: "anonymous" or "not released yet"
          // would claim the person hid themselves, and ADR-0020 §1 wants the
          // name absent rather than explained.
          <Typography
            variant="body2"
            color="text.secondary"
            sx={{ mb: 1, fontFamily: "monospace" }}
          >
            {gespraech.subject_id}
          </Typography>
        )}

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
          {gespraech.entry_month !== undefined && (
            <Typography
              // From stage 1. If the field is missing, NOTHING stands here — no
              // "not given", no "not released yet": from the outside the two are
              // indistinguishable, and that is exactly the promise.
              variant="body2"
            >
              {t("advisor.earliestStart")}: {gespraech.entry_month}
            </Typography>
          )}
          {gespraech.workload_percent !== undefined && (
            <Typography variant="body2">
              {t("advisor.workload")}: {gespraech.workload_percent}
            </Typography>
          )}
          {(gespraech.salary_min !== undefined || gespraech.salary_max !== undefined) && (
            <Typography
              // From stage 2 — absent and not released stay indistinguishable,
              // as above.
              variant="body2"
            >
              {gespraech.salary_min !== undefined && gespraech.salary_max !== undefined
                ? t("advisor.salaryRange", {
                    von: gespraech.salary_min,
                    bis: gespraech.salary_max,
                  })
                : gespraech.salary_min !== undefined
                  ? t("advisor.salaryFromOnly", { von: gespraech.salary_min })
                  : t("advisor.salaryToOnly", { bis: gespraech.salary_max })}
            </Typography>
          )}
        </Stack>

        <Typography variant="caption" sx={{ display: "block", mb: 1 }}>
          {t("advisor.handOverHint")}
        </Typography>

        <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
          {gespraech.state === "agreed" && (
            <Button
              variant="contained"
              size="small"
              disabled={laeuft}
              onClick={() => zug("hand-over")}
            >
              {t("advisor.handOver")}
            </Button>
          )}

          {offen && (
            <Button size="small" color="error" disabled={laeuft} onClick={() => zug("end")}>
              {t("advisor.end")}
            </Button>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}

export default CompanyAdvisorPage;
