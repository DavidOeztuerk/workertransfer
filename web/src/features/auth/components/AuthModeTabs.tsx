import { Link as RouterLink } from "react-router-dom";
import { useTranslation } from "react-i18next";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";

/**
 * The switch between signing in and registering — HERE, not in the header.
 *
 * Before, both paths stood at the top and hid each other depending on the
 * page. That is the wrong place: at the top they are two competing offers,
 * here they are <em>one decision with two answers</em> — and whoever got it
 * wrong sees the other path exactly where they are looking.
 *
 * Real links and no toggles: both states have an address of their own, can be
 * shared, and the back button does what it should.
 */
export function AuthModeTabs({ current }: { current: "login" | "register" }) {
  const { t } = useTranslation();

  return (
    <Tabs
      value={current}
      variant="fullWidth"
      sx={{ mb: 3, borderBottom: 1, borderColor: "divider" }}
      aria-label={t("login.tabsLabel")}
    >
      <Tab
        component={RouterLink}
        to="/login"
        value="login"
        label={t("login.tabSignIn")}
      />
      <Tab
        component={RouterLink}
        to="/register"
        value="register"
        label={t("login.tabRegister")}
      />
    </Tabs>
  );
}
