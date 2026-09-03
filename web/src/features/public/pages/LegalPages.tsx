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

/** <c>/imprint</c> — die Angaben nach § 5 DDG, und was davon fehlt. */
export function ImprintPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("recht.impressumTitel")} narrow lead={t("recht.impressumLead")}>
      <Alert severity="warning" sx={{ mb: 2.5 }}>
        <AlertTitle>{t("recht.impressumOffenTitel")}</AlertTitle>
        {t("recht.impressumOffenText")}
      </Alert>

      <Abschnitt titel={t("recht.impressumVerantwortlich")}>
        <Absatz>{t("recht.dsKontaktText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("recht.impressumStreit")}>
        <Absatz>{t("recht.impressumStreitText")}</Absatz>
      </Abschnitt>
    </PageShell>
  );
}

/** <c>/privacy</c> — vollständig, weil die Antworten in den ADRs stehen. */
export function PrivacyPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("recht.datenschutzTitel")} narrow lead={t("recht.datenschutzLead")}>
      <Abschnitt titel={t("recht.dsGrundsatzTitel")}>
        <Absatz>{t("recht.dsGrundsatzText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("recht.dsWasTitel")}>
        <Absatz>{t("recht.dsWasText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("recht.dsNichtTitel")}>
        <Punkte
          punkte={[
            t("recht.dsNicht1"),
            t("recht.dsNicht2"),
            t("recht.dsNicht3"),
            t("recht.dsNicht4"),
          ]}
        />
      </Abschnitt>

      <Abschnitt titel={t("recht.dsKiTitel")}>
        <Absatz>{t("recht.dsKiText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("recht.dsRechteTitel")}>
        <Absatz>{t("recht.dsRechteText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("recht.dsKontaktTitel")}>
        <Absatz>{t("recht.dsKontaktText")}</Absatz>
      </Abschnitt>
    </PageShell>
  );
}

/** <c>/terms</c> — was schon gilt, und was noch am Betreibermodell hängt. */
export function TermsPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("recht.agbTitel")} narrow lead={t("recht.agbLead")}>
      <Alert severity="warning" sx={{ mb: 2.5 }}>
        <AlertTitle>{t("recht.agbOffenTitel")}</AlertTitle>
        {t("recht.agbOffenText")}
      </Alert>

      <Abschnitt titel={t("recht.agbSchonTitel")}>
        <Punkte
          punkte={[t("recht.agb1"), t("recht.agb2"), t("recht.agb3"), t("recht.agb4")]}
        />
      </Abschnitt>
    </PageShell>
  );
}

/** <c>/accessibility</c> — mit den Lücken, denn eine Erklärung ohne sie ist keine. */
export function AccessibilityPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("recht.a11yTitel")} narrow lead={t("recht.a11yLead")}>
      <Abschnitt titel={t("recht.a11yStandTitel")}>
        <Absatz>{t("recht.a11yStandText")}</Absatz>
      </Abschnitt>

      <Abschnitt titel={t("recht.a11yTunTitel")}>
        <Punkte
          punkte={[t("recht.a11y1"), t("recht.a11y2"), t("recht.a11y3"), t("recht.a11y4")]}
        />
      </Abschnitt>

      <Abschnitt titel={t("recht.a11yLueckenTitel")}>
        <Absatz>{t("recht.a11yLuecken")}</Absatz>
      </Abschnitt>
    </PageShell>
  );
}
