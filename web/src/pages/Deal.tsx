import { useState } from 'react';
import { ActionIcon, Alert, Badge, Button, Card, Group, Menu, Modal, Stack, Text, TextInput, Textarea } from '@mantine/core';
import { IconCalendarPlus, IconDots, IconStar } from '@tabler/icons-react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, type Deal, type DealLeg } from '../api';
import { dateOnly, day, fromLocalInput, legStatusLabel, toLocalInput, todayIso, useAction, useApi, when } from '../lib';
import { Loading, Page, SectionTitle } from '../components/Layout';

type Dialog =
  | { kind: 'schedule'; leg: DealLeg }
  | { kind: 'done' | 'confirm'; leg: DealLeg }
  | { kind: 'callIn'; leg: DealLeg };

export function DealPage() {
  const { id } = useParams();
  const q = useApi<Deal>(['deal', id], `/deals/${id}`);
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const cancel = useAction(() => api(`/deals/${id}/cancel`, { method: 'POST' }), { success: 'Deal called off' });

  const canCancel = q.data?.status === 'open' &&
    q.data.legs.every((l) => l.status === 'open' || l.status === 'calledIn');

  return (
    <Page title="Deal" back right={canCancel && (
      <Menu position="bottom-end">
        <Menu.Target><ActionIcon variant="subtle" color="gray" size="lg" aria-label="More"><IconDots /></ActionIcon></Menu.Target>
        <Menu.Dropdown>
          <Menu.Item color="red" onClick={() => cancel.mutate()}>Call off this deal</Menu.Item>
        </Menu.Dropdown>
      </Menu>
    )}>
      <Loading query={q}>
        {() => {
          const d = q.data!;
          return (
            <Stack gap="sm">
              <Card>
                <Group justify="space-between" wrap="nowrap" align="flex-start">
                  <Text fw={650} size="lg">{d.request.title}</Text>
                  <Badge color={d.status === 'completed' ? 'teal' : d.status === 'cancelled' ? 'gray' : 'amber'}>
                    {d.status === 'open' ? 'In progress' : d.status === 'completed' ? 'All square' : 'Called off'}
                  </Badge>
                </Group>
                <Text size="sm" c="dimmed">
                  With <Link to={`/people/${d.counterparty.id}`}>{d.counterparty.name}</Link> · made {day(d.createdAt)}
                </Text>
                <Button component={Link} to={`/requests/${d.request.id}`} variant="subtle" color="gray" size="xs" mt={6} px={0}>
                  See the original request
                </Button>
              </Card>

              {d.counterparty.removedFromCircle && d.status === 'open' && (
                <Alert color="gray">
                  {d.counterparty.name} was removed from a circle you share. You can close out anything still open
                  between you instead of following up.
                </Alert>
              )}

              <SectionTitle>The two sides</SectionTitle>
              {d.legs.map((l) => (
                <LegCard key={l.id} leg={l} deal={d} onDialog={setDialog} />
              ))}

              {d.theirKids && <KidsCard name={d.counterparty.name} kids={d.theirKids} note={d.request.childcareNote} />}

              {d.endorsable.length > 0 && <EndorsePrompt name={d.counterparty.name} items={d.endorsable} />}
            </Stack>
          );
        }}
      </Loading>
      {dialog && <LegDialog dialog={dialog} onClose={() => setDialog(null)} />}
    </Page>
  );
}

function LegCard({ leg, deal, onDialog }: { leg: DealLeg; deal: Deal; onDialog: (d: Dialog) => void }) {
  const navigate = useNavigate();
  const status = legStatusLabel[leg.status];
  const outstanding = leg.status === 'open' || leg.status === 'calledIn' || leg.status === 'awaitingConfirmation';
  const owedToMe = !leg.iOwe;
  const closeOut = useAction(() => api(`/legs/${leg.id}/close-out`, { method: 'POST' }), { success: 'Closed out' });
  const canCloseOut = outstanding && (owedToMe || deal.counterparty.removedFromCircle);

  return (
    <Card className={(leg.iOwe && leg.status === 'calledIn') || (owedToMe && leg.status === 'awaitingConfirmation') ? 'pl-unread' : undefined}>
      <Group justify="space-between" wrap="nowrap" align="flex-start">
        <div>
          <Text size="sm" c="dimmed">{leg.iOwe ? `You owe ${leg.owedTo.name}` : `${leg.owedBy.name} owes you`}</Text>
          <Text fw={600}>{leg.description}</Text>
        </div>
        <Group gap={4} wrap="nowrap">
          {leg.kind === 'loose' && <Badge color="gray">Loose</Badge>}
          <Badge color={status.color}>{status.label}</Badge>
        </Group>
      </Group>

      {leg.scheduledAt && outstanding && (
        <Group gap="xs" mt="sm">
          <Text size="sm" fw={500}>{when(leg.scheduledAt)}</Text>
          <Button component="a" href={`/api/legs/${leg.id}/ics`} download variant="subtle" size="xs"
            leftSection={<IconCalendarPlus size={16} />}>Add to calendar</Button>
        </Group>
      )}
      {leg.completedOn && !outstanding && (
        <Text size="sm" mt="sm" c="dimmed">
          {leg.status === 'completed' ? 'Done' : 'Closed out'} {dateOnly(leg.completedOn)}
        </Text>
      )}
      {leg.status === 'awaitingConfirmation' && (
        <Text size="sm" mt="sm">
          {leg.iOwe ? `Waiting for ${leg.owedTo.name} to confirm.` : `${leg.owedBy.name} says it's done (${dateOnly(leg.completedOn)}).`}
        </Text>
      )}

      {outstanding && deal.status === 'open' && (
        <Group gap="xs" mt="md">
          {leg.iOwe && (leg.status === 'open' || leg.status === 'calledIn') && (
            <Button size="sm" onClick={() => onDialog({ kind: 'done', leg })}>Mark done</Button>
          )}
          {owedToMe && (
            <Button size="sm" variant={leg.status === 'awaitingConfirmation' ? 'filled' : 'default'}
              onClick={() => onDialog({ kind: 'confirm', leg })}>
              {leg.status === 'awaitingConfirmation' ? 'Confirm' : 'Mark complete'}
            </Button>
          )}
          {owedToMe && leg.status === 'open' && (
            <Button size="sm" variant="default" onClick={() => onDialog({ kind: 'callIn', leg })}>Call it in</Button>
          )}
          {leg.status === 'calledIn' && leg.calledInRequestId && (
            <Button size="sm" variant="subtle" onClick={() => navigate(`/requests/${leg.calledInRequestId}`)}>
              See the call-in
            </Button>
          )}
          <Menu position="bottom-end">
            <Menu.Target><ActionIcon variant="subtle" color="gray" size="lg" aria-label="More options"><IconDots size={18} /></ActionIcon></Menu.Target>
            <Menu.Dropdown>
              <Menu.Item onClick={() => onDialog({ kind: 'schedule', leg })}>{leg.scheduledAt ? 'Change time' : 'Set a time'}</Menu.Item>
              {canCloseOut && (
                <Menu.Item color="red" onClick={() => closeOut.mutate()}>
                  {owedToMe ? 'Let it go (close out)' : 'Close out'}
                </Menu.Item>
              )}
            </Menu.Dropdown>
          </Menu>
        </Group>
      )}
    </Card>
  );
}

function LegDialog({ dialog, onClose }: { dialog: Dialog; onClose: () => void }) {
  const { leg } = dialog;
  const [time, setTime] = useState(toLocalInput(leg.scheduledAt));
  const [date, setDate] = useState(leg.completedOn ?? todayIso());
  const [message, setMessage] = useState('');
  const navigate = useNavigate();

  const action = useAction(
    () => {
      switch (dialog.kind) {
        case 'schedule': return api(`/legs/${leg.id}/schedule`, { body: { scheduledAt: fromLocalInput(time) } });
        case 'done': return api(`/legs/${leg.id}/done`, { body: { completedOn: date } });
        case 'confirm': return api(`/legs/${leg.id}/confirm`, { body: { completedOn: date } });
        case 'callIn': return api<{ requestId: string }>(`/legs/${leg.id}/call-in`, { body: { message, proposedTime: fromLocalInput(time) } });
      }
    },
    {
      success: { schedule: 'Time saved', done: 'Marked done', confirm: 'Marked complete', callIn: 'Called in' }[dialog.kind],
      onSuccess: (r) => {
        onClose();
        if (dialog.kind === 'callIn') navigate(`/requests/${(r as { requestId: string }).requestId}`);
      },
    },
  );

  const titles = {
    schedule: 'When is it happening?',
    done: 'When did you do it?',
    confirm: 'When did it happen?',
    callIn: `Call in: ${leg.description}`,
  };

  return (
    <Modal opened onClose={onClose} title={titles[dialog.kind]} centered>
      <Stack>
        {(dialog.kind === 'schedule' || dialog.kind === 'callIn') && (
          <TextInput type="datetime-local" label="When" description={dialog.kind === 'callIn' ? `Suggest a time. ${leg.owedBy.name} can counter.` : undefined}
            value={time} onChange={(e) => setTime(e.currentTarget.value)} />
        )}
        {(dialog.kind === 'done' || dialog.kind === 'confirm') && (
          <TextInput type="date" label="Date" value={date} max={todayIso()} onChange={(e) => setDate(e.currentTarget.value)} />
        )}
        {dialog.kind === 'callIn' && (
          <Textarea label="Message" placeholder="e.g. Date night Saturday?" value={message}
            onChange={(e) => setMessage(e.currentTarget.value)} />
        )}
        {dialog.kind === 'schedule' && leg.scheduledAt && (
          <Text size="xs" c="dimmed">After changing the time, re-download the calendar file so your calendar updates.</Text>
        )}
        <Group grow>
          {dialog.kind === 'schedule' && leg.scheduledAt && (
            <Button variant="default" onClick={() => { setTime(''); }}>Clear</Button>
          )}
          <Button loading={action.isPending} disabled={(dialog.kind === 'done' || dialog.kind === 'confirm') && !date}
            onClick={() => action.mutate()}>
            {dialog.kind === 'callIn' ? 'Send' : 'Save'}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function KidsCard({ name, kids, note }: { name: string; kids: NonNullable<Deal['theirKids']>; note: string | null }) {
  return (
    <>
      <SectionTitle>{name}'s kids</SectionTitle>
      <Card>
        <Stack gap={6}>
          {kids.kidsAges && <Text size="sm"><Text span c="dimmed">Ages: </Text>{kids.kidsAges}</Text>}
          {kids.allergies && <Text size="sm"><Text span c="dimmed">Allergies: </Text>{kids.allergies}</Text>}
          {kids.contactNumber && (
            <Text size="sm"><Text span c="dimmed">Contact: </Text><a href={`tel:${kids.contactNumber}`}>{kids.contactNumber}</a></Text>
          )}
          {kids.notes && <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}><Text span c="dimmed">Notes: </Text>{kids.notes}</Text>}
          {note && <Text size="sm"><Text span c="dimmed">This time: </Text>{note}</Text>}
          <Text size="xs" c="dimmed" mt={4}>Only you can see this, and only while this deal is open.</Text>
        </Stack>
      </Card>
    </>
  );
}

function EndorsePrompt({ name, items }: { name: string; items: Deal['endorsable'] }) {
  const endorse = useAction((itemId: string) => api(`/items/${itemId}/endorse`, { method: 'POST' }), { success: 'Endorsed' });
  return (
    <Card mt="xs" bg="var(--mantine-color-amber-light)">
      <Text fw={600} mb={6}>Want to endorse {name}?</Text>
      <Text size="sm" c="dimmed" mb="sm">Totally optional. Endorsements help friends know who to ask.</Text>
      <Group gap="xs">
        {items.map((i) => (
          <Button key={i.id} size="xs" variant="default" leftSection={<IconStar size={14} />}
            loading={endorse.isPending && endorse.variables === i.id} onClick={() => endorse.mutate(i.id)}>
            {i.name}
          </Button>
        ))}
      </Group>
    </Card>
  );
}
