import Alert from "@mui/material/Alert";
import AlertTitle from "@mui/material/AlertTitle";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";

import { PageShell } from "../../../shared/components/ui";

/**
 * The four legal pages.
 *
 * <strong>They stand in ONE file because they are one kind:</strong> text, no
 * state, no fetch. Four files with twelve lines of scaffolding each would be
 * four places where the same scaffolding drifts apart.
 *
 * <strong>What is missing says so itself.</strong> Imprint and terms of use
 * hang on the operator's legal form, and that is not decided. An invented
 * postal address would be worse than a missing one — it would look like a
 * statement. The pages therefore name the gap instead of filling it.
 *
 * Privacy and accessibility, on the other hand, are complete: what this
 * platform does and refrains from stands in the ADRs and can thus be proven.
 */

/** A section with a heading — the same measure on all four pages. */
function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card sx={{ mb: 2.5 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1.5 }}>
          {title}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

function Paragraph({ children }: { children: React.ReactNode }) {
  return (
    <Typography variant="body2" color="text.secondary" sx={{ maxWidth: "76ch" }}>
      {children}
    </Typography>
  );
}

function Points({ points }: { points: string[] }) {
  return (
    <Box component="ul" sx={{ pl: 2.5, m: 0, display: "grid", gap: 1 }}>
      {points.map((point) => (
        <Typography
          component="li"
          variant="body2"
          color="text.secondary"
          key={point}
          sx={{ maxWidth: "74ch" }}
        >
          {point}
        </Typography>
      ))}
    </Box>
  );
}

/**
 * <c>/imprint</c> — the statements under § 5 DDG, and what of them is missing.
 *
 * The last section is the GeoNames attribution. It stands here and not only in
 * NOTICE.md because CC BY demands it towards the USERS, and nobody of them ever
 * sees a file in the source tree.
 */
export function ImprintPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("legal.imprintTitle")} narrow lead={t("legal.imprintLead")}>
      <Alert severity="warning" sx={{ mb: 2.5 }}>
        <AlertTitle>{t("legal.imprintIncompleteTitle")}</AlertTitle>
        {t("legal.imprintIncompleteText")}
      </Alert>

      <Section title={t("legal.imprintResponsible")}>
        <Paragraph>{t("legal.privacyContactText")}</Paragraph>
      </Section>

      <Section title={t("legal.imprintDisputes")}>
        <Paragraph>{t("legal.imprintDisputesText")}</Paragraph>
      </Section>

      <Section title={t("legal.imprintData")}>
        <Paragraph>{t("legal.imprintDataText")}</Paragraph>
      </Section>
    </PageShell>
  );
}

/** <c>/privacy</c> — complete, because the answers stand in the ADRs. */
export function PrivacyPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("legal.privacyTitle")} narrow lead={t("legal.privacyLead")}>
      <Section title={t("legal.privacyPrincipleTitle")}>
        <Paragraph>{t("legal.privacyPrincipleText")}</Paragraph>
      </Section>

      <Section title={t("legal.privacyStoredTitle")}>
        <Paragraph>{t("legal.privacyStoredText")}</Paragraph>
      </Section>

      <Section title={t("legal.privacyNotTitle")}>
        <Points
          points={[
            t("legal.privacyNot1"),
            t("legal.privacyNot2"),
            t("legal.privacyNot3"),
            t("legal.privacyNot4"),
          ]}
        />
      </Section>

      <Section title={t("legal.privacyAiTitle")}>
        <Paragraph>{t("legal.privacyAiText")}</Paragraph>
      </Section>

      <Section title={t("legal.privacyRightsTitle")}>
        <Paragraph>{t("legal.privacyRightsText")}</Paragraph>
      </Section>

      <Section title={t("legal.privacyContactTitle")}>
        <Paragraph>{t("legal.privacyContactText")}</Paragraph>
      </Section>
    </PageShell>
  );
}

/** <c>/terms</c> — what already holds, and what still hangs on the operator model. */
export function TermsPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("legal.termsTitle")} narrow lead={t("legal.termsLead")}>
      <Alert severity="warning" sx={{ mb: 2.5 }}>
        <AlertTitle>{t("legal.termsIncompleteTitle")}</AlertTitle>
        {t("legal.termsIncompleteText")}
      </Alert>

      <Section title={t("legal.termsAlreadyTitle")}>
        <Points
          points={[t("legal.terms1"), t("legal.terms2"), t("legal.terms3"), t("legal.terms4")]}
        />
      </Section>
    </PageShell>
  );
}

/** <c>/accessibility</c> — with the gaps, because a statement without them is none. */
export function AccessibilityPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("legal.a11yTitle")} narrow lead={t("legal.a11yLead")}>
      <Section title={t("legal.a11yStatusTitle")}>
        <Paragraph>{t("legal.a11yStatusText")}</Paragraph>
      </Section>

      <Section title={t("legal.a11yAppliesTitle")}>
        <Points
          points={[t("legal.a11y1"), t("legal.a11y2"), t("legal.a11y3"), t("legal.a11y4")]}
        />
      </Section>

      <Section title={t("legal.a11yGapsTitle")}>
        <Paragraph>{t("legal.a11yGaps")}</Paragraph>
      </Section>
    </PageShell>
  );
}
