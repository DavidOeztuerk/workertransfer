import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card, Field } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type Role,
  inviteMember,
  listInvitations,
  listMembers,
  removeMember,
  withdrawInvitation,
} from "../auth/team";

export interface TeamRouteProps {
  principal?: MeResponse | null;
}

export function TeamRoute({ principal = null }: TeamRouteProps) {
  const queryClient = useQueryClient();
  const tenantId = principal?.tenant_id ?? null;

  const membersQuery = useQuery({
    queryKey: ["team", "members", tenantId],
    queryFn: () => listMembers(tenantId as string),
    enabled: tenantId !== null,
  });
  const invitationsQuery = useQuery({
    queryKey: ["team", "invitations", tenantId],
    queryFn: () => listInvitations(tenantId as string),
    enabled: tenantId !== null,
  });

  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("member");
  // Zwei Fehlerzustände, nicht einer: die Meldungen gehören zu verschiedenen
  // Karten, und ein gemeinsamer Zustand rendert dieselbe Meldung zweimal auf
  // der Seite — einmal neben der Mannschaft und einmal unter dem Formular.
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [memberError, setMemberError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  const invite = useMutation({
    mutationFn: () => inviteMember(tenantId as string, email, role),
    onSuccess: (result) => {
      if (result.ok) {
        setInviteError(null);
        // Bewusst dieselbe Meldung, ob die Adresse ein Konto hat oder nicht:
        // der Server antwortet in beiden Fällen gleich, und ein Unterschied
        // hier wäre genau das Leck, das er vermeidet.
        setSent(true);
        setEmail("");
        void queryClient.invalidateQueries({ queryKey: ["team", "invitations", tenantId] });
      } else {
        setSent(false);
        setInviteError(result.message);
      }
    },
  });

  const withdraw = useMutation({
    mutationFn: (invitationId: string) => withdrawInvitation(tenantId as string, invitationId),
    onSuccess: (result) => {
      setInviteError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["team", "invitations", tenantId] });
    },
  });

  const remove = useMutation({
    mutationFn: (memberId: string) => removeMember(tenantId as string, memberId),
    onSuccess: (result) => {
      setMemberError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["team", "members", tenantId] });
    },
  });

  if (tenantId === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Mannschaft</h1>
          <p>
            Wähle oben ein Unternehmen — oder <a href="/company/new">lege eines an</a>.
          </p>
        </Card>
      </main>
    );
  }

  const members = membersQuery.data;
  const invitations = invitationsQuery.data;
  // Die eigene Rolle steht in der Mannschaftsliste, nicht im Token: dort steht
  // nur, FÜR welches Unternehmen jemand handelt, nicht mit welcher Berechtigung.
  const myRole = members?.ok
    ? members.members.find((entry) => entry.user_id === principal?.user_id)?.role
    : undefined;
  const isAdmin = myRole === "admin";

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Mannschaft</h1>
        <p className="page__lead">
          Wer hier steht, kann für das Unternehmen handeln — Profile sehen, Lebensläufe anfragen.
          Administratoren dürfen außerdem einladen.
        </p>
      </header>

      <Card>
        <h2>Mitglieder</h2>
        {members !== undefined && !members.ok ? (
          <p className="auth__alert" role="alert">
            {members.message}
          </p>
        ) : null}
        {members?.ok ? (
          <ul className="team">
            {members.members.map((entry) => (
              <li key={entry.user_id}>
                <span>{entry.display_name}</span>
                <span className="team__role">
                  {entry.role === "admin" ? "Administrator" : "Mitglied"}
                </span>
                {isAdmin ? (
                  <Button
                    variant="quiet"
                    onClick={() => remove.mutate(entry.user_id)}
                    disabled={remove.isPending}
                  >
                    {entry.user_id === principal?.user_id ? "Verlassen" : "Entfernen"}
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}
        {memberError !== null ? (
          <p className="auth__alert" role="alert">
            {memberError}
          </p>
        ) : null}
      </Card>

      {isAdmin ? (
        <Card>
          <h2>Einladen</h2>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              invite.mutate();
            }}
          >
            <Field
              label="E-Mail-Adresse"
              type="email"
              hint="Die Person braucht ein Konto mit genau dieser Adresse — sie kann es auch nach der Einladung anlegen."
              value={email}
              onChange={(e) => {
                setSent(false);
                setEmail(e.target.value);
              }}
              required
            />
            <label className="wt-field">
              <span className="wt-field__label">Rolle</span>
              <select
                className="wt-field__input"
                value={role}
                onChange={(e) => setRole(e.target.value as Role)}
              >
                <option value="member">Mitglied</option>
                <option value="admin">Administrator</option>
              </select>
            </label>

            {inviteError !== null ? (
              <p className="auth__alert" role="alert">
                {inviteError}
              </p>
            ) : null}
            {sent && inviteError === null ? (
              <p className="page__note">Einladung verschickt.</p>
            ) : null}

            <Button type="submit" disabled={invite.isPending}>
              {invite.isPending ? "Wird verschickt…" : "Einladen"}
            </Button>
          </form>
        </Card>
      ) : null}

      <Card>
        <h2>Offene Einladungen</h2>
        {invitations?.ok && invitations.invitations.length === 0 ? (
          <p>Keine offenen Einladungen.</p>
        ) : null}
        {invitations?.ok && invitations.invitations.length > 0 ? (
          <ul className="team" data-testid="invitation-list">
            {invitations.invitations.map((entry) => (
              <li key={entry.id}>
                <span>{entry.email}</span>
                <span className="team__role">
                  {entry.role === "admin" ? "Administrator" : "Mitglied"}
                </span>
                {isAdmin ? (
                  <Button
                    variant="quiet"
                    onClick={() => withdraw.mutate(entry.id)}
                    disabled={withdraw.isPending}
                  >
                    Zurückziehen
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}
      </Card>
    </main>
  );
}
