import Alert from "@mui/material/Alert";
import AlertTitle from "@mui/material/AlertTitle";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";

import { PageShell } from "../../../shared/components/ui";

/**
 * Die vier rechtlichen Seiten.
 *
 * <strong>Sie stehen in EINER Datei, weil sie eine Sorte sind:</strong> Text,
 * kein Zustand, keine Abfrage. Vier Dateien mit je zwölf Zeilen Gerüst wären
 * vier Stellen, an denen dasselbe Gerüst auseinanderläuft.
 *
 * <strong>Was fehlt, sagt sich selbst.</strong> Impressum und
 * Nutzungsbedingungen hängen an der Rechtsform des Betreibers, und die ist nicht
 * entschieden. Eine erfundene Anschrift wäre schlimmer als eine fehlende — sie
 * sähe aus wie eine Angabe. Die Seiten benennen deshalb die Lücke, statt sie zu
 * füllen.
 *
 * Datenschutz und Barrierefreiheit sind dagegen vollständig: was diese
 * Plattform tut und lässt, steht in den ADRs und ist damit belegbar.
 */

/** Ein Abschnitt mit Überschrift — auf allen vier Seiten dasselbe Mass. */
function Abschnitt({ titel, children }: { titel: string; children: React.ReactNode }) {
  return (
    <Card sx={{ mb: 2.5 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1.5 }}>
          {titel}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

function Absatz({ children }: { children: React.ReactNode }) {
  return (
    <Typography variant="body2" color="text.secondary" sx={{ maxWidth: "76ch" }}>
      {children}
    </Typography>
  );
}

function Punkte({ punkte }: { punkte: string[] }) {
  return (
    <Box component="ul" sx={{ pl: 2.5, m: 0, display: "grid", gap: 1 }}>
      {punkte.map((punkt) => (
        <Typography
          component="li"
          variant="body2"
          color="text.secondary"
          key={punkt}
          sx={{ maxWidth: "74ch" }}
        >
          {punkt}
        </Typography>
      ))}
    </Box>
  );
}

/**
 * <c>/imprint</c> — die Angaben nach § 5 DDG, und was davon fehlt.
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

      <Abschnitt titel={t("legal.imprintResponsible")}>
        <Absatz>{t("legal.privacyContactText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("legal.imprintDisputes")}>
        <Absatz>{t("legal.imprintDisputesText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("legal.imprintData")}>
        <Absatz>{t("legal.imprintDataText")}</Absatz>
      </Abschnitt>
    </PageShell>
  );
}

/** <c>/privacy</c> — vollständig, weil die Antworten in den ADRs stehen. */
export function PrivacyPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("legal.privacyTitle")} narrow lead={t("legal.privacyLead")}>
      <Abschnitt titel={t("legal.privacyPrincipleTitle")}>
        <Absatz>{t("legal.privacyPrincipleText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("legal.privacyStoredTitle")}>
        <Absatz>{t("legal.privacyStoredText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("legal.privacyNotTitle")}>
        <Punkte
          punkte={[
            t("legal.privacyNot1"),
            t("legal.privacyNot2"),
            t("legal.privacyNot3"),
            t("legal.privacyNot4"),
          ]}
        />
      </Abschnitt>

      <Abschnitt titel={t("legal.privacyAiTitle")}>
        <Absatz>{t("legal.privacyAiText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("legal.privacyRightsTitle")}>
        <Absatz>{t("legal.privacyRightsText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("legal.privacyContactTitle")}>
        <Absatz>{t("legal.privacyContactText")}</Absatz>
      </Abschnitt>
    </PageShell>
  );
}

/** <c>/terms</c> — was schon gilt, und was noch am Betreibermodell hängt. */
export function TermsPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("legal.termsTitle")} narrow lead={t("legal.termsLead")}>
      <Alert severity="warning" sx={{ mb: 2.5 }}>
        <AlertTitle>{t("legal.termsIncompleteTitle")}</AlertTitle>
        {t("legal.termsIncompleteText")}
      </Alert>

      <Abschnitt titel={t("legal.termsAlreadyTitle")}>
        <Punkte
          punkte={[t("legal.terms1"), t("legal.terms2"), t("legal.terms3"), t("legal.terms4")]}
        />
      </Abschnitt>
    </PageShell>
  );
}

/** <c>/accessibility</c> — mit den Lücken, denn eine Erklärung ohne sie ist keine. */
export function AccessibilityPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("legal.a11yTitle")} narrow lead={t("legal.a11yLead")}>
      <Abschnitt titel={t("legal.a11yStatusTitle")}>
        <Absatz>{t("legal.a11yStatusText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("legal.a11yAppliesTitle")}>
        <Punkte
          punkte={[t("legal.a11y1"), t("legal.a11y2"), t("legal.a11y3"), t("legal.a11y4")]}
        />
      </Abschnitt>

      <Abschnitt titel={t("legal.a11yGapsTitle")}>
        <Absatz>{t("legal.a11yGaps")}</Absatz>
      </Abschnitt>
    </PageShell>
  );
}
