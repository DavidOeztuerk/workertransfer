import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Trans } from "react-i18next";
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
 * <strong>`satz` ist ein ganzer Satz, kein Zweck-Fragment.</strong> Vorher stand
 * hier „Bitte anmelden, um " + zweck + "." — eine Fuge, die nur im Deutschen
 * aufgeht: Englisch und Französisch bauen den Satz anders, und der Verweis sitzt
 * nicht an derselben Stelle. Der Schlüssel benennt deshalb den vollständigen
 * Satz, und <c>&lt;1&gt;</c> darin ist der Verweis.
 *
 * Der Verweis ist ein Router-Link, kein `<a href>`: ein voller Neuladevorgang
 * mitten in der Anwendung verwirft die geladene Sitzung und fragt sie neu.
 */
export function AnmeldungNoetig({
  titel,
  satz,
}: {
  titel: string;
  satz: string;
}) {
  return (
    <PageShell title={titel} narrow>
      <Card>
        <CardContent>
          <Typography>
            <Trans
              i18nKey={satz}
              components={{ 1: <Link component={RouterLink} to="/login" /> }}
            />
          </Typography>
        </CardContent>
      </Card>
    </PageShell>
  );
}
