import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { failureKey } from "../../../shared/api/errors";
import { listCompanyApplications } from "../api/applications";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";

export function CompanyApplicationsPage() {
  const { t } = useTranslation();
  const { forCompany } = useActor();
  const list = useAsync(
    (signal) => listCompanyApplications(signal),
    [],
    forCompany,
  );

  if (!forCompany) {
    return (
      <PageShell title={t("companyApplications.listTitle")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("companyApplications.companyOnly")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  const items = list.data?.ok ? list.data.items : [];

  return (
    <PageShell title={t("companyApplications.listTitle")} narrow lead={t("companyApplications.listLead")}>
      <Card>
        <CardContent>
          {list.pending ? <LoadingBlock label={t("companyApplications.loading")} /> : null}
          {list.data !== null && !list.data.ok ? (
            <Alert severity="error">{t(failureKey(list.data.error, "errors.listNotLoaded"))}</Alert>
          ) : null}
          {list.data?.ok && items.length === 0 ? (
            <EmptyBlock title={t("companyApplications.listEmpty")} />
          ) : null}
          <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 1.5 }}>
            {items.map((row) => (
              <Card key={row.id} component="li" variant="outlined">
                <CardContent
                  sx={{ display: "flex", justifyContent: "space-between", gap: 2, flexWrap: "wrap" }}
                >
                  <Box>
                    <Typography variant="h4">{row.job_title}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {t(`applications.status${row.status.charAt(0).toUpperCase()}${row.status.slice(1)}`)}
                    </Typography>
                  </Box>
                  <Button
                    component={RouterLink}
                    to={`/company/applications/${row.id}`}
                    variant="contained"
                    size="small"
                  >
                    {t("companyApplications.open")}
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
