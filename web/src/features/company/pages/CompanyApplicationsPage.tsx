import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { listCompanyApplications } from "../api/applications";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";

export function CompanyApplicationsPage() {
  const { t } = useTranslation();
  const { fuerFirma } = useHandelnder();
  const liste = useAsync(
    (signal) => listCompanyApplications(signal),
    [],
    fuerFirma,
  );

  if (!fuerFirma) {
    return (
      <PageShell title={t("firmenbewerbung.listeTitel")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("firmenbewerbung.nurFirma")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  const items = liste.data?.ok ? liste.data.items : [];

  return (
    <PageShell title={t("firmenbewerbung.listeTitel")} narrow lead={t("firmenbewerbung.listeLead")}>
      <Card>
        <CardContent>
          {liste.pending ? <LoadingBlock label={t("firmenbewerbung.laden")} /> : null}
          {liste.data !== null && !liste.data.ok ? (
            <Alert severity="error">{liste.data.error.detail}</Alert>
          ) : null}
          {liste.data?.ok && items.length === 0 ? (
            <EmptyBlock title={t("firmenbewerbung.listeLeer")} />
          ) : null}
          <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 1.5 }}>
            {items.map((zeile) => (
              <Card key={zeile.id} component="li" variant="outlined">
                <CardContent
                  sx={{ display: "flex", justifyContent: "space-between", gap: 2, flexWrap: "wrap" }}
                >
                  <Box>
                    <Typography variant="h4">{zeile.job_title}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {t(`bewerbungen.stand${zeile.status.charAt(0).toUpperCase()}${zeile.status.slice(1)}`)}
                    </Typography>
                  </Box>
                  <Button
                    component={RouterLink}
                    to={`/company/applications/${zeile.id}`}
                    variant="contained"
                    size="small"
                  >
                    {t("firmenbewerbung.oeffnen")}
                  </Button>
                </CardContent>
              </Card>
            ))}
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}
