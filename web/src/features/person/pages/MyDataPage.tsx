import Alert from "@mui/material/Alert";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { SignInRequired } from "../components/SignInRequired";
import { useAsync } from "../lib/useAsync";
import {
  type ExportSection,
  buildExport,
  fileName,
  section,
} from "../lib/export";
import type { MessageKey } from "../../../shared/api/errors";
import { listMyConsentHistory, listMyConsents } from "../api/consent";
import { getMyProfile } from "../api/profile";
import {
  getDocumentTerms,
  getMyCurriculumVitae,
  getMyDocuments,
  getMyResumeRequests,
} from "../api/resume";
import { getAccountSettings } from "../api/accountSettings";
import { getLinks } from "../../auth/api/externalLogin";
import { getMandate, getMyConversations } from "../../work/api/advisor";
import { getMyCases } from "../../work/api/assessments";
import { getEmployerRoute } from "../../work/api/releases";
import { getMyNotice } from "../../work/api/market";
import { getDrafts } from "../../work/api/applications";
import { getConversation, listConversations } from "../../messages/api/messages";
import { getMyPortfolio } from "../api/portfolio";
import { getMyConnection } from "../api/github";
import { getNotificationPreferences } from "../api/notificationPreferences";
import { getPostalAddress } from "../api/civilIdentity";
import { getMyMarketStatus, listMyMarketRequests } from "../../work/api/market";
import { listMyTransfers } from "../../work/api/transfers";
import { listMyApplications } from "../../work/api/applications";

/**
 * <c>/my-data</c> — everything about oneself, in one file.
 *
 * <strong>It is built in the browser and stored nowhere.</strong> There is no
 * endpoint that produces it — otherwise an export of one's own data would
 * itself have left data behind.
 *
 * <strong>Twelve sources, in parallel, and none breaks the page.</strong>
 * Whatever does not answer is marked <c>nicht_abrufbar</c> — on the page AND in
 * the file. An export that silently leaves out half is worse than one that is
 * missing: the one is noticed, the other is not.
 *
 * <strong>No delete button next to the download button.</strong> Nothing here
 * can be clicked wrongly that could not be undone. The link goes in both
 * directions, so that nobody believes there is a mandatory order.
 */
/**
 * What the page CALLS each section — the catalogue's word, in the person's
 * language.
 *
 * <strong>The file keeps its German section names</strong> (`profil`,
 * `freigaben_verlauf` …): they are keys of a data format somebody may already
 * have parsed, and consents-journey reads them from the downloaded file. The
 * page drew exactly those keys until 26.09.2026 (`name.replace(/_/g, " ")`),
 * so an English or French person read "freigaben verlauf". The type makes the
 * two lists one: a section without a label does not compile.
 */
const SECTION_LABELS = {
  konto: "myData.sectionAccount",
  benachrichtigungen: "myData.sectionNotifications",
  profil: "myData.sectionProfile",
  lebenslauf: "myData.sectionResume",
  lebenslauf_anfragen: "myData.sectionResumeRequests",
  portfolio: "myData.sectionPortfolio",
  github: "myData.sectionGitHub",
  marktstatus: "myData.sectionMarketStatus",
  markt_anfragen: "myData.sectionMarketRequests",
  transfers: "myData.sectionTransfers",
  bewerbungen: "myData.sectionApplications",
  freigaben: "myData.sectionConsents",
  freigaben_verlauf: "myData.sectionConsentHistory",
  dokumente: "myData.sectionDocuments",
  dokument_begriffe: "myData.sectionDocumentTerms",
  entwuerfe: "myData.sectionDrafts",
  nachrichten: "myData.sectionMessages",
  arbeitsproben: "myData.sectionAssessments",
  mandat: "myData.sectionMandate",
  berater_gespraeche: "myData.sectionAdvisorConversations",
  kuendigungsfrist: "myData.sectionNotice",
  arbeitgeber_weg: "myData.sectionEmployerRoute",
  anmeldewege: "myData.sectionSignInWays",
  einstellungen: "myData.sectionSettings",
} as const satisfies Record<string, MessageKey>;

type SectionName = keyof typeof SECTION_LABELS;

const labelOf = (name: string): MessageKey =>
  SECTION_LABELS[name as SectionName] ?? "myData.sectionOther";

export function MyDataPage() {
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  const dataExport = useAsync(
    async (signal): Promise<Record<SectionName, ExportSection>> => {
      const [
        notifications,
        postalAddress,
        profile,
        curriculumVitae,
        resumeRequests,
        portfolio,
        github,
        marketStatus,
        marketRequests,
        transfers,
        applications,
        consents,
        history,
        documents,
        documentTerms,
        drafts,
        conversations,
        assessments,
        mandate,
        advisorConversations,
        notice,
        employerRoute,
        signInWays,
        settings,
      ] = await Promise.all([
        getNotificationPreferences(signal),
        getPostalAddress(signal),
        getMyProfile(signal),
        getMyCurriculumVitae(signal),
        getMyResumeRequests(signal),
        getMyPortfolio(signal),
        getMyConnection(signal),
        getMyMarketStatus(signal),
        listMyMarketRequests(signal),
        listMyTransfers(signal),
        listMyApplications(signal),
        listMyConsents(signal),
        listMyConsentHistory(signal),
        getMyDocuments(signal),
        getDocumentTerms(signal),
        getDrafts(signal),
        messagesWithText(signal),
        getMyCases(signal),
        getMandate(signal),
        getMyConversations(signal),
        getMyNotice(signal),
        getEmployerRoute(signal),
        getLinks(signal),
        getAccountSettings(signal),
      ]);

      return {
        // The keys of this object are the SECTION NAMES of the file a person
        // downloads — a data contract, not an incidental name. They are German
        // because they were at the first export, and the E2E journey reads them
        // off the page (`consents-journey.spec.ts`).
        konto: section(session !== null, {
          ...session,
          anschrift: postalAddress ?? undefined,
        }),
        benachrichtigungen: section(
          notifications !== null,
          notifications ?? undefined,
        ),
        profil: section(profile.ok, profile.ok ? profile.profile : undefined),
        lebenslauf: section(
          curriculumVitae.ok,
          curriculumVitae.ok ? curriculumVitae.value : undefined,
        ),
        lebenslauf_anfragen: section(
          resumeRequests.ok,
          resumeRequests.ok ? resumeRequests.value : undefined,
        ),
        portfolio: section(
          portfolio.ok,
          portfolio.ok ? portfolio.value : undefined,
        ),
        github: section(github.ok, github.ok ? github.value : undefined),
        marktstatus: section(
          marketStatus.ok,
          marketStatus.ok ? marketStatus.status : undefined,
        ),
        markt_anfragen: section(
          marketRequests.ok,
          marketRequests.ok ? marketRequests.requests : undefined,
        ),
        transfers: section(
          transfers.ok,
          transfers.ok ? transfers.transfers : undefined,
        ),
        bewerbungen: section(
          applications.ok,
          applications.ok ? applications.applications : undefined,
        ),
        freigaben: section(
          consents.ok,
          consents.ok ? consents.consents : undefined,
        ),
        freigaben_verlauf: section(
          history.ok,
          history.ok ? history.events : undefined,
        ),
        // The list of documents, not their bytes: those stay where they are and
        // open from the CV page — a file of all certificates inlined would be
        // megabytes of base64 nobody can read.
        dokumente: section(documents.ok, documents.ok ? documents.value : undefined),
        dokument_begriffe: section(documentTerms.ok, documentTerms.ok ? documentTerms.value : undefined),
        entwuerfe: section(drafts.ok, drafts.ok ? drafts.drafts : undefined),
        nachrichten: section(conversations !== null, conversations ?? undefined),
        arbeitsproben: section(assessments.ok, assessments.ok ? assessments.items : undefined),
        mandat: section(mandate.ok, mandate.ok ? mandate.mandate : undefined),
        berater_gespraeche: section(
          advisorConversations.ok,
          advisorConversations.ok ? advisorConversations.items : undefined,
        ),
        kuendigungsfrist: section(notice.ok, notice.ok ? notice.notice : undefined),
        arbeitgeber_weg: section(employerRoute.ok, employerRoute.ok ? employerRoute.route : undefined),
        anmeldewege: section(signInWays.ok, signInWays.ok ? signInWays.links : undefined),
        // Whether a key is stored, never the key or its tail: a secret in a
        // downloaded file is one more copy of it, on a disk we do not know.
        einstellungen: section(
          settings !== null,
          settings === null
            ? undefined
            : {
                deleteAfterMonths: settings.deleteAfterMonths,
                aiProvider: settings.provider,
                aiModel: settings.model,
                aiKeyStored: settings.keyPresent,
                auditLog: settings.auditLog,
              },
        ),
      };
    },
    [session?.userId],
    session !== null,
  );

  if (status === "anonymous") {
    return (
      <SignInRequired
        title={t("myData.title")}
        sentenceKey="myData.signInRequired"
      />
    );
  }

  const result =
    dataExport.value === undefined ? null : buildExport(dataExport.value);
  const missing = result?.unvollständig ?? [];

  function download() {
    if (result === null) return;

    const content = new Blob([JSON.stringify(result, null, 2)], {
      type: "application/json",
    });
    const url = URL.createObjectURL(content);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName();
    link.click();
    // Release it, otherwise the browser keeps the content in memory until the
    // next reload — needlessly long for a file about one's own data.
    URL.revokeObjectURL(url);
  }

  return (
    <PageShell
      title={t("myData.title")}
      narrow
      lead={t("myData.lead")}
    >
      <Card sx={{ mb: 3 }}>
        <CardContent>
          {dataExport.loading ? (
            <LoadingBlock label={t("myData.loading")} />
          ) : null}

          {missing.length > 0 ? (
            <Alert severity="warning" sx={{ mb: 2 }}>
              {t("myData.incomplete", {
                teile: missing.map((name) => t(labelOf(name))).join(", "),
              })}
            </Alert>
          ) : null}

          {result !== null ? (
            <>
              <Box
                // A <dl> and not a list: every section carries whether it is
                // included, and that pairing IS the content, so it belongs in
                // the markup rather than in the layout.
                component="dl"
                sx={{
                  display: "grid",
                  gridTemplateColumns: "auto 1fr",
                  columnGap: 3,
                  rowGap: 0.75,
                  mb: 3,
                }}
              >
                {Object.entries(result.abschnitte).map(([name, entry]) => (
                  <Box key={name} sx={{ display: "contents" }}>
                    <Typography component="dt" variant="body2">
                      {t(labelOf(name))}
                    </Typography>
                    <Typography
                      component="dd"
                      variant="body2"
                      sx={{ m: 0 }}
                      color={
                        entry.status === "ok"
                          ? "text.secondary"
                          : "warning.main"
                      }
                    >
                      {t(
                        entry.status === "ok"
                          ? "myData.included"
                          : "myData.missing",
                      )}
                    </Typography>
                  </Box>
                ))}
              </Box>

              <Button variant="contained" onClick={download}>
                {t("myData.download")}
              </Button>
            </>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>
            {t("myData.deleteTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("myData.deleteText")}
          </Typography>
          <Button
            component={RouterLink}
            to="/delete-account"
            variant="outlined"
            color="error"
          >
            {t("header.deleteAccount")}
          </Button>
        </CardContent>
      </Card>
    </PageShell>
  );
}

/**
 * Every conversation with its messages — `null` when the list itself could not
 * be read. A single conversation that fails stays in the list without its
 * messages rather than taking the whole section down.
 */
async function messagesWithText(signal?: AbortSignal) {
  const list = await listConversations(signal);
  if (!list.ok) return null;

  return Promise.all(
    list.items.map(async (summary) => {
      const conversation = await getConversation(summary.reference, signal, summary.kind);
      return conversation.ok ? { ...summary, ...conversation.conversation } : summary;
    }),
  );
}

