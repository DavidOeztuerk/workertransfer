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

import { LetterSheet } from "../../../shared/components/LetterSheet";
import { getCompanyProfile } from "../api/companies";
import { recipientFromCompany, senderFromContact } from "../../work/lib/letterHead";
import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { failureKey } from "../../../shared/api/errors";
import { CurriculumVitaeSheet } from "../../person/components/resume/CurriculumVitaeSheet";
import { getVisibleCurriculumVitae, getVisibleDocumentContent } from "../../person/api/resume";
import { getCandidateProfile } from "../api/candidateProfile";
import { getCompanyApplication } from "../api/applications";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import { getJob } from "../../work/api/jobs";
import { ConversationPanel } from "../../messages/components/ConversationPanel";

export function CompanyApplicationPage() {
  const { t } = useTranslation();
  const { forCompany } = useActor();
  const { id } = useParams<{ id: string }>();
  const [tab, setTab] = useState(0);

  const application = useAsync(
    (signal) => getCompanyApplication(id!, signal),
    [id],
    forCompany && !!id,
  );

  const e = application.data?.ok ? application.data.application : null;

  const job = useAsync(
    (signal) => (e ? getJob(e.job_id, signal) : Promise.resolve(null)),
    [e?.job_id],
    e !== null,
  );
  const company = useAsync(
    (signal) =>
      job.data
        ? getCompanyProfile(job.data.tenant_id, signal)
        : Promise.resolve(null),
    [job.data?.tenant_id],
    job.data !== null,
  );

  const profile = useAsync(
    (signal) => (e ? getCandidateProfile(e.subject_id, signal) : Promise.resolve(null)),
    [e?.subject_id],
    e !== null,
  );

  const curriculumVitae = useAsync(
    (signal) =>
      e?.shares_resume
        ? getVisibleCurriculumVitae(e.subject_id, signal)
        : Promise.resolve({ ok: true as const, value: null }),
    [e?.subject_id, e?.shares_resume],
    e !== null,
  );

  if (!forCompany || !id) {
    return (
      <PageShell title={t("companyApplications.title")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("companyApplications.companyOnly")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (application.pending && e === null) {
    return (
      <PageShell title={t("companyApplications.title")} narrow>
        <LoadingBlock label={t("companyApplications.loading")} />
      </PageShell>
    );
  }

  if (application.data !== null && !application.data.ok) {
    return (
      <PageShell title={t("companyApplications.title")} narrow>
        <Alert severity="error">
          {t(failureKey(application.data.error, "errors.companyApplicationNotLoaded"))}
        </Alert>
      </PageShell>
    );
  }

  if (!e) {
    return (
      <PageShell title={t("companyApplications.title")} narrow>
        <EmptyBlock
          title={t("companyApplications.notFound")}
          hint={t("companyApplications.notFoundHint")}
        />
      </PageShell>
    );
  }

  const cv = curriculumVitae.data?.ok ? curriculumVitae.data.value : null;
  const tabs: { key: string; label: string }[] = [
    { key: "letter", label: t("companyApplications.tabCoverLetter") },
  ];
  if (e.shares_resume && cv) {
    tabs.push({ key: "resume", label: t("companyApplications.tabResume") });
  }
  for (const docId of e.documents) {
    tabs.push({ key: `doc-${docId}`, label: t("companyApplications.tabDocument") });
  }
  tabs.push({ key: "messages", label: t("companyApplications.tabMessages") });

  const current = tabs[tab] ?? tabs[0] ?? { key: "letter", label: t("companyApplications.tabCoverLetter") };

  return (
    <PageShell title={job.data?.title ?? t("companyApplications.title")}>
      <Card sx={{ mb: 2 }}>
        <CardContent>
          <Typography variant="h4" sx={{ mb: 0.5 }}>
            {profile.data?.headline || t("companyApplications.noHeadline")}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {job.data?.title ?? t("companyApplications.jobUnknown")}
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
            {tabs.map((entry) => (
              <Tab key={entry.key} label={entry.label} />
            ))}
          </Tabs>

          {current.key === "letter" ? (
            <LetterSheet
              subject={undefined}
              text={e.message}
              sender={senderFromContact(e.applicant_contact)}
              recipient={recipientFromCompany(company.data, job.data)}
            />
          ) : null}

          {current.key === "resume" && cv ? (
            <CurriculumVitaeSheet
              template={cv.template}
              data={{
                headline: profile.data?.headline ?? "",
                bio: profile.data?.bio ?? "",
                location: profile.data?.location ?? "",
                skills: profile.data?.skills ?? [],
                positions: cv.positions,
                education: cv.education,
              }}
            />
          ) : null}

          {current.key === "messages" ? <ConversationPanel application={e.id} /> : null}

          {current.key.startsWith("doc-") ? (
            <DocumentView
              subjectId={e.subject_id}
              documentId={current.key.slice(4)}
            />
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  );
}

function DocumentView({
  subjectId,
  documentId,
}: {
  subjectId: string;
  documentId: string;
}) {
  const { t } = useTranslation();
  const [url, setUrl] = useState<string | null>(null);
  const [type, setType] = useState<string>("");

  useEffect(() => {
    let objectUrl: string | null = null;
    let gone = false;
    void getVisibleDocumentContent(subjectId, documentId).then((result) => {
      if (gone || !result.ok) return;
      objectUrl = result.value.url;
      setUrl(result.value.url);
      setType(result.value.contentType);
    });
    return () => {
      gone = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [subjectId, documentId]);

  if (url === null) {
    return <LoadingBlock label={t("companyApplications.loading")} />;
  }

  if (type.startsWith("image/")) {
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
      title={t("companyApplications.tabDocument")}
      sx={{ width: "100%", height: "70vh", border: 0 }}
    />
  );
}
