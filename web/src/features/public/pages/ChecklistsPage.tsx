import { Fragment } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";

import { PageShell } from "../../../shared/components/ui";
import { ReferenceLetter } from "../components/ReferenceLetter";

/**
 * The lists, with their points written out as keys — never built from a
 * number, so every key stands in the catalogue where a test can find it.
 */
const LISTS = [
  {
    title: "checklists.noticeTitle",
    points: ["checklists.notice1", "checklists.notice2", "checklists.notice3", "checklists.notice4"],
  },
  {
    title: "checklists.agreementTitle",
    points: ["checklists.agreement1", "checklists.agreement2", "checklists.agreement3", "checklists.agreement4"],
  },
  {
    title: "checklists.suspensionTitle",
    points: ["checklists.suspension1", "checklists.suspension2", "checklists.suspension3"],
  },
  {
    title: "checklists.referenceTitle",
    points: ["checklists.reference1", "checklists.reference2", "checklists.reference3"],
  },
  {
    title: "checklists.holidayTitle",
    points: ["checklists.holiday1", "checklists.holiday2"],
  },
] as const;

/**
 * Neutral places, and only those (VISION-FRAGEN F2): no partner lawyers — a
 * referral fee is forbidden (§ 49b BRAO), and a recommended lawyer would be a
 * commission in disguise.
 */
const PLACES = [
  { label: "checklists.placeAgency", href: "https://www.arbeitsagentur.de/arbeitslos-arbeit-finden/arbeitslosengeld" },
  { label: "checklists.placeLawyer", href: "https://anwaltauskunft.de" },
  { label: "checklists.placeUnion", href: "https://www.dgb-rechtsschutz.de" },
  { label: "checklists.placeMinistry", href: "https://www.bmas.de/DE/Arbeit/Arbeitsrecht/arbeitsrecht.html" },
] as const;

/**
 * <c>/checklists</c> — what to watch when leaving a job (VISION-FRAGEN B6, F2).
 *
 * <strong>General, never about one contract.</strong> Explaining rules is
 * allowed to everybody; judging a concrete contract is a legal service and
 * belongs to a lawyer (§ 2 RDG). So the page says what the rules are and,
 * above every list, that a contract is checked before it is signed. Lawyer
 * question J6 covers whether this wording stays on the right side.
 *
 * Public, without sign-in: whoever thinks about leaving should not have to
 * register first to learn what a notice must look like.
 */
export function ChecklistsPage() {
  const { t } = useTranslation();

  return (
    <PageShell title={t("checklists.title")} lead={t("checklists.lead")} narrow>
      <Alert severity="warning" sx={{ mb: 3 }}>
        {t("checklists.checkFirst")}
      </Alert>

      {LISTS.map((list) => (
        <Fragment key={list.title}>
        <Card sx={{ mb: 2.5 }}>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1.5 }}>
              {t(list.title)}
            </Typography>
            <Box component="ul" sx={{ pl: 2.5, m: 0, display: "grid", gap: 1 }}>
              {list.points.map((point) => (
                <Typography component="li" variant="body2" color="text.secondary" key={point} sx={{ maxWidth: "74ch" }}>
                  {t(point)}
                </Typography>
              ))}
            </Box>
          </CardContent>
        </Card>
        {/* The sample letter stands under the list it belongs to (C2). */}
        {list.title === "checklists.referenceTitle" ? <ReferenceLetter /> : null}
        </Fragment>
      ))}

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1.5 }}>
            {t("checklists.placesTitle")}
          </Typography>
          <Box component="ul" sx={{ pl: 2.5, m: 0, display: "grid", gap: 1 }}>
            {PLACES.map((place) => (
              <li key={place.href}>
                <Link href={place.href} target="_blank" rel="noopener noreferrer">
                  {t(place.label)}
                </Link>
              </li>
            ))}
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
            {t("checklists.placesNote")}
          </Typography>
        </CardContent>
      </Card>
    </PageShell>
  );
}
