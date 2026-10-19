import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import TextField from "@mui/material/TextField";

import { draftProfileText } from "../api/profile";

/**
 * Writing help — on request, and it says what goes out.
 *
 * The three decisions visible here:
 *
 * 1. <strong>Only at the press of a button.</strong> No suggestion by itself,
 *    no background run, no memory. The same rule as for GitHub: looking once
 *    when asked is something different from watching permanently.
 * 2. <strong>The notice stands at the button</strong>, not in a privacy
 *    policy. Whoever presses it has read that their text goes to a third-party
 *    provider.
 * 3. <strong>The draft replaces only the form field.</strong> It is saved only
 *    when the person presses "Save" — then it is their text. There is no ledger
 *    entry: the button IS the consent, informed and per use.
 *
 * And one deliberate detail: if they have already written something, the
 * button says "rephrase" instead of "write". A button that overwrites existing
 * work unasked gets pressed once and never again.
 */
export function DraftHelp({
  onDraft,
  hasText,
}: {
  onDraft: (draft: string) => void;
  hasText: boolean;
}) {
  const { t } = useTranslation();
  const [wish, setWish] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  async function ask() {
    setRunning(true);
    const result = await draftProfileText(wish);
    setRunning(false);
    if (result.ok) {
      setProblem(null);
      onDraft(result.draft);
    } else {
      setProblem(result.message);
    }
  }

  return (
    <Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
      <TextField
        label={t(hasText ? "draftHelp.labelRephrase" : "draftHelp.labelNew")}
        helperText={t("draftHelp.hint")}
        value={wish}
        onChange={(event) => setWish(event.target.value)}
        slotProps={{ htmlInput: { maxLength: 200 } }}
        fullWidth
      />
      {problem !== null ? <Alert severity="error">{problem}</Alert> : null}
      <Box>
        <Button
          // Spelled out because this button sits in the same <form> as "Save";
          // without it the click would submit the profile instead of helping.
          type="button"
          variant="text"
          onClick={() => void ask()}
          disabled={running}
        >
          {running
            ? t("draftHelp.inProgress")
            : t(hasText ? "draftHelp.buttonReplaces" : "draftHelp.buttonNew")}
        </Button>
      </Box>
    </Box>
  );
}
