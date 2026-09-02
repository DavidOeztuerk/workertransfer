import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";

/**
 * „Bitte anmelden" — auf jeder Seite dieses Bereichs derselbe Satz.
 *
 * Er steht einmal, weil neun Seiten ihn neun Mal leicht verschieden sagen
 * würden, und weil eine dieser neun `/delete-account` ist: dort darf er
 * <strong>nach</strong> einer erfolgreichen Löschung nicht mehr erscheinen. Eine
 * gemeinsame Stelle macht sichtbar, wer ihn wann zeichnet.
 *
 * Der Verweis ist ein Router-Link, kein `<a href>`: ein voller Neuladevorgang
 * mitten in der Anwendung verwirft die geladene Sitzung und fragt sie neu.
 */
export function AnmeldungNoetig({ titel, zweck }: { titel: string; zweck: string }) {
  return (
    <PageShell title={titel} narrow>
      <Card>
        <CardContent>
          <Typography>
            Bitte{" "}
            <Link component={RouterLink} to="/login">
              anmelden
            </Link>
            , um {zweck}.
          </Typography>
        </CardContent>
      </Card>
    </PageShell>
  );
}
