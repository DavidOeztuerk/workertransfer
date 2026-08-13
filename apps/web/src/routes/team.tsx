import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Empty, Loading, Page, Row, RowList } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
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

  // Zwei Fehlerzustände, nicht einer: die Meldungen gehören zu verschiedenen
  // Karten, und ein gemeinsamer Zustand rendert dieselbe Meldung zweimal auf
  // der Seite.
  const [memberError, setMemberError] = useState<string | null>(null);
  const [invitationError, setInvitationError] = useState<string | null>(null);

  const withdraw = useMutation({
    mutationFn: (invitationId: string) => withdrawInvitation(tenantId as string, invitationId),
    onSuccess: (result) => {
      setInvitationError(result.ok ? null : result.message);
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
      <Page title="Mannschaft" narrow>
        <Card>
          <p>
            Wähle oben ein Unternehmen — oder lass dich von jemandem aus deinem Unternehmen
            einladen.
          </p>
        </Card>
      </Page>
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
    <Page
      title="Mannschaft"
      narrow
      lead="Wer hier steht, kann für das Unternehmen handeln — Profile sehen, Lebensläufe anfragen. Administratoren dürfen außerdem einladen."
    >
      <Card>
        <h2>Mitglieder</h2>
        {/* Der LADEZUSTAND fehlte: `isPending` wurde nicht geprüft, und die
            Karte blieb währenddessen leer. */}
        {membersQuery.isPending ? <Loading label="Mitglieder werden geladen…" /> : null}
        {members !== undefined && !members.ok ? <Alert>{members.message}</Alert> : null}
        {members?.ok ? (
          <RowList>
            {members.members.map((entry) => (
              <Row
                key={entry.user_id}
                title={entry.display_name}
                meta={entry.role === "admin" ? "Administrator" : "Mitglied"}
                actions={
                  isAdmin ? (
                    <Button
                      variant="quiet"
                      onClick={() => remove.mutate(entry.user_id)}
                      disabled={remove.isPending}
                    >
                      {entry.user_id === principal?.user_id ? "Verlassen" : "Entfernen"}
                    </Button>
                  ) : undefined
                }
              />
            ))}
          </RowList>
        ) : null}
        {memberError !== null ? <Alert>{memberError}</Alert> : null}
        {isAdmin ? <Button href="/company/team/invite">Einladen</Button> : null}
      </Card>

      <Card>
        <h2>Offene Einladungen</h2>
        {invitationError !== null ? <Alert>{invitationError}</Alert> : null}
        {invitations?.ok && invitations.invitations.length === 0 ? (
          <Empty title="Keine offenen Einladungen." />
        ) : null}
        {invitations?.ok && invitations.invitations.length > 0 ? (
          <RowList className="team" data-testid="invitation-list">
            {invitations.invitations.map((entry) => (
              <Row
                key={entry.id}
                title={entry.email}
                meta={entry.role === "admin" ? "Administrator" : "Mitglied"}
                actions={
                  isAdmin ? (
                    <Button
                      variant="quiet"
                      onClick={() => withdraw.mutate(entry.id)}
                      disabled={withdraw.isPending}
                    >
                      Zurückziehen
                    </Button>
                  ) : undefined
                }
              />
            ))}
          </RowList>
        ) : null}
      </Card>
    </Page>
  );
}
