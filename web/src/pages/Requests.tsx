import { useState } from 'react';
import { Alert, Badge, Button, Card, Group, SegmentedControl, Stack, Text } from '@mantine/core';
import { IconBabyCarriage, IconBell } from '@tabler/icons-react';
import { Link } from 'react-router-dom';
import { api, type Invite, type RequestSummary } from '../api';
import { ago, requestStatusLabel, useAction, useApi, useMe, windowText } from '../lib';
import { Empty, Loading, Page } from '../components/Layout';

export function RequestsPage() {
  const [view, setView] = useState('open');
  const me = useMe();
  const requests = useApi<RequestSummary[]>(['requests', view], `/requests?view=${view}`);
  const invites = useApi<Invite[]>(['invites'], '/invites');
  const noCircles = me.data?.counts.circles === 0;

  return (
    <Page title="Requests">
      <Stack gap="sm">
        {invites.data?.map((i) => <InviteBanner key={i.id} invite={i} />)}

        <SegmentedControl
          fullWidth
          value={view}
          onChange={setView}
          data={[
            { value: 'open', label: 'Open' },
            { value: 'direct', label: 'For me' },
            { value: 'mine', label: 'Mine' },
          ]}
        />

        <Loading query={requests}>
          {() =>
            requests.data!.length === 0 ? (
              noCircles ? (
                <Empty title="No circles yet">
                  Start a circle and invite your friends from the <Link to="/circles">Circles</Link> tab.
                </Empty>
              ) : (
                <Empty title={view === 'mine' ? "You haven't posted anything" : 'Nothing open right now'}>
                  Need a hand, a ladder, or a night off? Tap + to post a request.
                </Empty>
              )
            ) : (
              requests.data!.map((r) => <RequestCard key={r.id} r={r} meId={me.data!.user.id} />)
            )
          }
        </Loading>
      </Stack>
    </Page>
  );
}

function InviteBanner({ invite }: { invite: Invite }) {
  const accept = useAction(() => api(`/invites/${invite.id}/accept`, { method: 'POST' }), { success: `Welcome to ${invite.circle.name}` });
  const decline = useAction(() => api(`/invites/${invite.id}/decline`, { method: 'POST' }));
  return (
    <Alert color="amber" icon={<IconBell size={18} />} title={`${invite.invitedBy} invited you to ${invite.circle.name}`}>
      <Group gap="xs" mt="xs">
        <Button size="sm" loading={accept.isPending} onClick={() => accept.mutate()}>Join</Button>
        <Button size="sm" variant="subtle" color="gray" loading={decline.isPending} onClick={() => decline.mutate()}>No thanks</Button>
      </Group>
    </Alert>
  );
}

export function RequestCard({ r, meId }: { r: RequestSummary; meId: string }) {
  const status = requestStatusLabel[r.status];
  const where = r.recipient
    ? r.recipient.id === meId ? 'to you' : `to ${r.recipient.name}`
    : r.circle?.name;
  const timing = windowText(r.neededFrom, r.neededTo);
  return (
    <Link to={`/requests/${r.id}`} className="pl-card-link">
      <Card className={r.awaitingMe ? 'pl-unread' : undefined}>
        <Group justify="space-between" align="flex-start" wrap="nowrap" gap="xs">
          <Text fw={600} lineClamp={2}>{r.title}</Text>
          <Group gap={4} wrap="nowrap">
            {r.awaitingMe && <Badge color="amber" variant="filled">Your move</Badge>}
            {!r.awaitingMe && r.status !== 'open' && <Badge color={status.color}>{status.label}</Badge>}
          </Group>
        </Group>
        <Text size="sm" c="dimmed" mt={2}>
          {r.author.id === meId ? 'You' : r.author.name} · {where} · {ago(r.createdAt)}
        </Text>
        {timing && <Text size="sm" mt={6}>{timing}</Text>}
        {r.offers.length > 0 && (
          <Text size="sm" mt={6} lineClamp={2}>
            <Text span c="dimmed">In exchange: </Text>{r.offers.join(' or ')}
          </Text>
        )}
        <Group gap={6} mt={8}>
          {r.isChildcare && <Badge color="gray" leftSection={<IconBabyCarriage size={12} />}>Childcare</Badge>}
          {r.isCalledIn && <Badge color="amber">IOU called in</Badge>}
          {r.suggestedRate && <Badge color="gray">{r.suggestedRate}</Badge>}
          {r.author.id === meId && r.responderCount > 0 && (
            <Badge color="gray">{r.responderCount} {r.responderCount === 1 ? 'response' : 'responses'}</Badge>
          )}
        </Group>
      </Card>
    </Link>
  );
}
