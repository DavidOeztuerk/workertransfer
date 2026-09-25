import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Trans } from "react-i18next";
import { Link as RouterLink } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";

/**
 * "Please sign in" — the same sentence on every page of this area.
 *
 * It stands once, because nine pages would say it nine slightly different
 * ways, and because one of those nine is `/delete-account`: there it must no
 * longer appear <strong>after</strong> a successful erasure. One shared place
 * makes visible who draws it when.
 *
 * <strong>`sentenceKey` names a whole sentence, not a purpose fragment.</strong>
 * This used to be "Bitte anmelden, um " + purpose + "." — a joint that only
 * works in German: English and French build the sentence differently, and the
 * link does not sit in the same place. The key therefore names the complete
 * sentence, and <c>&lt;1&gt;</c> in it is the link.
 *
 * The link is a router link, not an `<a href>`: a full reload in the middle of
 * the application discards the loaded session and asks for it again.
 */
export function SignInRequired({
  title,
  sentenceKey,
}: {
  title: string;
  sentenceKey: string;
}) {
  return (
    <PageShell title={title} narrow>
      <Card>
        <CardContent>
          <Typography>
            <Trans
              i18nKey={sentenceKey}
              components={{ 1: <Link component={RouterLink} to="/login" /> }}
            />
          </Typography>
        </CardContent>
      </Card>
    </PageShell>
  );
}
