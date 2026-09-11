import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import Typography from "@mui/material/Typography";
import { useParams } from "react-router-dom";

import { Briefbogen } from "../../../shared/components/Briefbogen";
import { getCompanyProfile } from "../api/companies";
import { empfaengerAusFirma, kopfAusKontakt } from "../../work/lib/briefkopf";
import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { Lebenslaufblatt } from "../../person/components/lebenslauf/Lebenslaufblatt";
import { holeFremdenInhalt, ladeSichtbaren } from "../../person/api/resume";
import { getCandidateProfile } from "../api/fremdprofil";
import { getCompanyApplication } from "../api/applications";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import { getJob } from "../../work/api/jobs";

export function CompanyApplicationPage() {
  const { t } = useTranslation();
  const { fuerFirma } = useHandelnder();
  const { id } = useParams<{ id: string }>();
  const [tab, setTab] = useState(0);

  const bewerbung = useAsync(
    (signal) => getCompanyApplication(id!, signal),
    [id],
    fuerFirma && !!id,
  );

  const e = bewerbung.data?.ok ? bewerbung.data.application : null;

  const stelle = useAsync(
    (signal) => (e ? getJob(e.job_id, signal) : Promise.resolve(null)),
    [e?.job_id],
    e !== null,
  );
  const firma = useAsync(
    (signal) =>
      stelle.data
        ? getCompanyProfile(stelle.data.tenant_id, signal)
        : Promise.resolve(null),
    [stelle.data?.tenant_id],
    stelle.data !== null,
  );

  const profil = useAsync(
    (signal) => (e ? getCandidateProfile(e.subject_id, signal) : Promise.resolve(null)),
    [e?.subject_id],
    e !== null,
  );

  const lebenslauf = useAsync(
    (signal) =>
      e?.shares_resume
        ? ladeSichtbaren(e.subject_id, signal)
        : Promise.resolve({ ok: true as const, value: null }),
    [e?.subject_id, e?.shares_resume],
    e !== null,
  );

  if (!fuerFirma || !id) {
    return (
      <PageShell title={t("firmenbewerbung.titel")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("firmenbewerbung.nurFirma")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (bewerbung.pending && e === null) {
    return (
      <PageShell title={t("firmenbewerbung.titel")} narrow>
        <LoadingBlock label={t("firmenbewerbung.laden")} />
      </PageShell>
    );
  }

  if (bewerbung.data !== null && !bewerbung.data.ok) {
    return (
      <PageShell title={t("firmenbewerbung.titel")} narrow>
        <Alert severity="error">{bewerbung.data.error.detail}</Alert>
      </PageShell>
    );
  }

  if (!e) {
    return (
      <PageShell title={t("firmenbewerbung.titel")} narrow>
        <EmptyBlock
          title={t("firmenbewerbung.nichtGefunden")}
          hint={t("firmenbewerbung.nichtGefundenHinweis")}
        />
      </PageShell>
    );
  }

  const cv = lebenslauf.data?.ok ? lebenslauf.data.value : null;
  const reiter: { key: string; label: string }[] = [
    { key: "letter", label: t("firmenbewerbung.tabAnschreiben") },
  ];
  if (e.shares_resume && cv) {
    reiter.push({ key: "resume", label: t("firmenbewerbung.tabLebenslauf") });
  }
  for (const docId of e.documents) {
    reiter.push({ key: `doc-${docId}`, label: t("firmenbewerbung.tabUnterlage") });
  }

  const aktuell = reiter[tab] ?? reiter[0] ?? { key: "letter", label: t("firmenbewerbung.tabAnschreiben") };

  return (
    <PageShell title={stelle.data?.title ?? t("firmenbewerbung.titel")}>
      <Card sx={{ mb: 2 }}>
        <CardContent>
          <Typography variant="h4" sx={{ mb: 0.5 }}>
            {profil.data?.headline || t("firmenbewerbung.ohneUeberschrift")}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {stelle.data?.title ?? t("firmenbewerbung.stelleUnbekannt")}
          </Typography>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Tabs
            value={tab}
            onChange={(_, next) => setTab(next)}
            variant="scrollable"
            scrollButtons="auto"
            sx={{ borderBottom: 1, borderColor: "divider", mb: 2 }}
          >
            {reiter.map((r) => (
              <Tab key={r.key} label={r.label} />
            ))}
          </Tabs>

          {aktuell.key === "letter" ? (
            <Briefbogen
              betreff={undefined}
              text={e.message}
              kopf={kopfAusKontakt(e.applicant_contact)}
              empfaenger={empfaengerAusFirma(firma.data, stelle.data)}
            />
          ) : null}

          {aktuell.key === "resume" && cv ? (
            <Lebenslaufblatt
              template={cv.template}
              daten={{
                headline: profil.data?.headline ?? "",
                bio: profil.data?.bio ?? "",
                location: profil.data?.location ?? "",
                skills: profil.data?.skills ?? [],
                positions: cv.positions,
                education: cv.education,
              }}
            />
          ) : null}

          {aktuell.key.startsWith("doc-") ? (
            <Unterlagenansicht
              subjectId={e.subject_id}
              documentId={aktuell.key.slice(4)}
            />
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  );
}

function Unterlagenansicht({
  subjectId,
  documentId,
}: {
  subjectId: string;
  documentId: string;
}) {
  const { t } = useTranslation();
  const [url, setUrl] = useState<string | null>(null);
  const [typ, setTyp] = useState<string>("");

  useEffect(() => {
    let frei: string | null = null;
    let weg = false;
    void holeFremdenInhalt(subjectId, documentId).then((result) => {
      if (weg || !result.ok) return;
      frei = result.value.url;
      setUrl(result.value.url);
      setTyp(result.value.contentType);
    });
    return () => {
      weg = true;
      if (frei) URL.revokeObjectURL(frei);
    };
  }, [subjectId, documentId]);

  if (url === null) {
    return <LoadingBlock label={t("firmenbewerbung.laden")} />;
  }

  if (typ.startsWith("image/")) {
    return (
      <Box
        component="img"
        src={url}
        alt=""
        sx={{ maxWidth: "100%", maxHeight: "70vh", display: "block", mx: "auto" }}
      />
    );
  }

  return (
    <Box
      component="iframe"
      src={url}
      title={t("firmenbewerbung.tabUnterlage")}
      sx={{ width: "100%", height: "70vh", border: 0 }}
    />
  );
}
