import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardActionArea from "@mui/material/CardActionArea";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { useSearchParams } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { failureKey } from "../../../shared/api/errors";
import { useActor } from "../../company/lib/session";
import { useAsync } from "../../company/lib/useAsync";
import { getCompanyProfile } from "../../company/api/companies";
import { getCandidateProfile } from "../../company/api/candidateProfile";
import { type ConversationSummary, type ProcessKind, listConversations } from "../api/messages";
import { ConversationPanel } from "../components/ConversationPanel";

/** The kind as a catalogue key. */
const KIND_KEY = {
  application: "messages.kindApplication",
  approach: "messages.kindApproach",
  initiative: "messages.kindInitiative",
} as const satisfies Record<ProcessKind, string>;

/**
 * <c>/messages</c> — every conversation of the caller, person or company
 * (ADR-0047).
 *
 * <strong>No unread count and no "seen".</strong> The list is ordered by the
 * last message and says with whom and about what, nothing more. Opening one
 * asks the process's own service — or, for an approach, the ledger — again.
 */
export function MessagesPage() {
  const { t } = useTranslation();
  const { signedIn, forCompany } = useActor();
  const [params, setParams] = useSearchParams();
  const kind = (params.get("kind") ?? "") as ProcessKind | "";
  const reference = params.get("reference") ?? "";

  const list = useAsync((signal) => listConversations(signal), [forCompany], signedIn);

  if (!signedIn) {
    return (
      <PageShell title={t("messages.title")} narrow>
        <Typography>{t("messages.signInRequired")}</Typography>
      </PageShell>
    );
  }

  const items = list.data?.ok ? list.data.items : [];

  return (
    <PageShell title={t("messages.title")} lead={t("messages.lead")}>
      {list.pending && items.length === 0 ? <LoadingBlock label={t("messages.loading")} /> : null}
      {list.data !== null && !list.data.ok ? (
        <Alert severity="error">{t(failureKey(list.data.error, "errors.listNotLoaded"))}</Alert>
      ) : null}
      {list.data?.ok && items.length === 0 ? <EmptyBlock title={t("messages.noConversations")} /> : null}

      <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { xs: "1fr", md: "minmax(240px, 1fr) 2fr" } }}>
        <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 1, alignContent: "start" }}>
          {items.map((item) => (
            <Card
              component="li"
              key={`${item.kind}-${item.reference}`}
              variant={item.reference === reference ? "elevation" : "outlined"}
            >
              <CardActionArea onClick={() => setParams({ kind: item.kind, reference: item.reference })}>
                <CardContent>
                  <Counterpart item={item} />
                  <Typography variant="body2" color="text.secondary">
                    {t(KIND_KEY[item.kind])}
                    {" · "}
                    {new Date(item.last_message_at).toLocaleString()}
                  </Typography>
                </CardContent>
              </CardActionArea>
            </Card>
          ))}
        </Box>

        {kind !== "" && reference !== "" ? (
          <Card>
            <CardContent>
              <ConversationPanel key={`${kind}-${reference}`} kind={kind} reference={reference} />
            </CardContent>
          </Card>
        ) : null}
      </Box>
    </PageShell>
  );
}

/**
 * With whom. For the person the company's public name; for the company the
 * person's headline — which answers only while the person releases it, so a
 * revoked release reads "a person" and nothing more.
 */
function Counterpart({ item }: { item: ConversationSummary }) {
  const { t } = useTranslation();
  const company = useAsync(
    (signal) => (item.company_id ? getCompanyProfile(item.company_id, signal) : Promise.resolve(null)),
    [item.company_id],
    item.company_id !== undefined,
  );
  const person = useAsync(
    (signal) => (item.person_id ? getCandidateProfile(item.person_id, signal) : Promise.resolve(null)),
    [item.person_id],
    item.person_id !== undefined,
  );

  const name = item.company_id !== undefined
    ? company.data?.display_name || t("messages.aCompany")
    : person.data?.headline || t("messages.aPerson");

  return <Typography variant="h4">{name}</Typography>;
}
