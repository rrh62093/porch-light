import { useState } from 'react';
import {
  Alert, Badge, Button, Card, Checkbox, Group, Menu, Modal, Radio, Stack, Text, TextInput, Textarea, ActionIcon,
} from '@mantine/core';
import { IconBabyCarriage, IconDots } from '@tabler/icons-react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, type CounterMsg, type RequestDetail } from '../api';
import { ago, fromLocalInput, requestStatusLabel, toLocalInput, useAction, useApi, useMe, when, windowText } from '../lib';
import { Loading, Page, SectionTitle } from '../components/Layout';

type Mode = { kind: 'accept' | 'counter'; responderId?: string };

export function RequestDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const me = useMe();
  const q = useApi<RequestDetail>(['request', id], `/requests/${id}`);
  const [mode, setMode] = useState<Mode | null>(null);

  const close = useAction(() => api(`/requests/${id}/close`, { method: 'POST' }), { success: 'Request closed' });
  const pass = useAction(() => api(`/requests/${id}/respond`, { body: { kind: 'decline' } }), { success: 'Passed' });

  return (
    <Page title="Request" back right={q.data?.isAuthor && (q.data.status === 'open' || q.data.status === 'inNegotiation') && (
      <Menu position="bottom-end">
        <Menu.Target><ActionIcon variant="subtle" color="gray" size="lg" aria-label="More"><IconDots /></ActionIcon></Menu.Target>
        <Menu.Dropdown>
          <Menu.Item color="red" onClick={() => close.mutate()}>
            {q.data.calledIn ? 'Withdraw call-in' : 'Close request'}
          </Menu.Item>
        </Menu.Dropdown>
      </Menu>
    )}>
      <Loading query={q}>
        {() => {
          const r = q.data!;
          const meId = me.data!.user.id;
          const status = requestStatusLabel[r.status];
          const timing = windowText(r.neededFrom, r.neededTo);
          return (
            <Stack gap="sm">
              <Card>
                <Group justify="space-between" align="flex-start" wrap="nowrap">
                  <Text fw={650} size="lg">{r.title}</Text>
                  <Badge color={status.color}>{status.label}</Badge>
                </Group>
                <Text size="sm" c="dimmed" mt={2}>
                  {r.isAuthor ? 'You' : <Link to={`/people/${r.author.id}`}>{r.author.name}</Link>}
                  {' · '}
                  {r.recipient ? (r.recipient.id === meId ? 'to you' : `to ${r.recipient.name}`) : r.circle?.name}
                  {' · '}{ago(r.createdAt)}
                </Text>
                {r.calledIn && (
                  <Alert color="amber" mt="sm" p="xs">
                    Calling in an IOU{r.calledIn.kind === 'loose' ? ' ("you owe me one")' : `: ${r.calledIn.description}`}
                  </Alert>
                )}
                {r.description && <Text mt="sm" style={{ whiteSpace: 'pre-wrap' }}>{r.description}</Text>}
                {timing && <Text mt="sm" fw={500}>{timing}</Text>}
                <Group gap={6} mt="sm">
                  {r.isChildcare && <Badge color="gray" leftSection={<IconBabyCarriage size={12} />}>Childcare</Badge>}
                  {r.suggestedRate && <Badge color="gray">Suggested: {r.suggestedRate}</Badge>}
                </Group>
                {r.childcareNote && (
                  <Text size="sm" mt="sm"><Text span c="dimmed">Note: </Text>{r.childcareNote}</Text>
                )}
              </Card>

              {r.offers.length > 0 && (
                <>
                  <SectionTitle>In exchange</SectionTitle>
                  <Card p="sm">
                    <Stack gap={6}>
                      {r.offers.map((o) => (
                        <Group key={o.id} gap="xs" wrap="nowrap">
                          <Text>• {o.description}</Text>
                          {o.isChildcare && <Badge size="xs" color="gray">Childcare</Badge>}
                        </Group>
                      ))}
                    </Stack>
                  </Card>
                </>
              )}

              {r.dealId && (
                <Button component={Link} to={`/deals/${r.dealId}`} variant="light" size="md">
                  {r.calledIn ? 'View the original deal' : 'View the deal'}
                </Button>
              )}

              {r.canRespond && (
                <Group grow mt="xs">
                  <Button onClick={() => setMode({ kind: 'accept' })}>{r.calledIn ? 'Agree' : 'Accept'}</Button>
                  <Button variant="default" onClick={() => setMode({ kind: 'counter' })}>
                    {r.calledIn ? 'Suggest a time' : 'Counter'}
                  </Button>
                  {r.recipient?.id === meId && !r.calledIn && (
                    <Button variant="subtle" color="gray" loading={pass.isPending} onClick={() => pass.mutate()}>Pass</Button>
                  )}
                </Group>
              )}

              {(r.threads.length > 0 || r.isAuthor) && <SectionTitle>{r.isAuthor ? 'Responses' : 'Your conversation'}</SectionTitle>}
              {r.isAuthor && r.threads.length === 0 && r.status !== 'closed' && (
                <Text c="dimmed" size="sm">No responses yet. You'll get a notification when someone answers.</Text>
              )}
              {r.threads.map((t) => (
                <Thread key={t.responder.id} r={r} meId={meId} responder={t.responder} messages={t.messages}
                  onCounter={() => setMode({ kind: 'counter', responderId: t.responder.id })} />
              ))}
            </Stack>
          );
        }}
      </Loading>

      {q.data && mode && (
        <RespondModal r={q.data} mode={mode} onClose={() => setMode(null)}
          onDeal={(dealId) => { setMode(null); if (dealId) navigate(`/deals/${dealId}`); }} />
      )}
    </Page>
  );
}

function terms(m: CounterMsg): string | null {
  if (m.loose) return 'You owe me one';
  return m.customText ?? m.selectedOffer;
}

function Thread({ r, meId, responder, messages, onCounter }: {
  r: RequestDetail; meId: string; responder: { id: string; name: string }; messages: CounterMsg[]; onCounter: () => void;
}) {
  const navigate = useNavigate();
  const pending = [...messages].reverse().find((m) => m.status === 'pending');
  const theirMove = pending && pending.author.id === meId;
  const myMove = pending && pending.author.id !== meId;
  const accept = useAction(
    () => api<{ dealId: string }>(`/requests/${r.id}/counters/${pending!.id}/accept`, { method: 'POST' }),
    { success: 'Deal made', onSuccess: (res) => res.dealId && navigate(`/deals/${res.dealId}`) },
  );
  const decline = useAction(() => api(`/requests/${r.id}/counters/${pending!.id}/decline`, { method: 'POST' }));

  return (
    <Card className={myMove ? 'pl-unread' : undefined}>
      {r.isAuthor && <Text fw={600} mb="xs"><Link to={`/people/${responder.id}`}>{responder.name}</Link></Text>}
      <Stack gap="sm">
        {messages.map((m) => {
          const who = m.author.id === meId ? 'You' : m.author.name;
          const verb = m.kind === 'accept' ? 'accepted' : m.kind === 'decline' ? 'passed' : 'countered';
          const t = terms(m);
          return (
            <div key={m.id}>
              <Group gap={6}>
                <Text size="sm" fw={600}>{who} {verb}</Text>
                <Text size="xs" c="dimmed">{ago(m.createdAt)}</Text>
                {m.kind === 'counter' && m.status !== 'pending' && (
                  <Badge size="xs" color={m.status === 'accepted' ? 'teal' : 'gray'}>{m.status}</Badge>
                )}
              </Group>
              {t && <Text size="sm">{t}{m.customIsChildcare ? ' (childcare)' : ''}</Text>}
              {m.proposedTime && <Text size="sm">{when(m.proposedTime)}</Text>}
              {m.message && <Text size="sm" c="dimmed" style={{ whiteSpace: 'pre-wrap' }}>"{m.message}"</Text>}
            </div>
          );
        })}
      </Stack>
      {myMove && (r.status === 'open' || r.status === 'inNegotiation') && (
        <Group mt="md" gap="xs">
          <Button size="sm" loading={accept.isPending} onClick={() => accept.mutate()}>Accept</Button>
          {r.isAuthor && <Button size="sm" variant="default" onClick={onCounter}>Counter</Button>}
          <Button size="sm" variant="subtle" color="gray" loading={decline.isPending} onClick={() => decline.mutate()}>Decline</Button>
        </Group>
      )}
      {theirMove && <Text size="sm" c="dimmed" mt="sm">Waiting on {r.isAuthor ? responder.name : r.author.name}.</Text>}
    </Card>
  );
}

function RespondModal({ r, mode, onClose, onDeal }: {
  r: RequestDetail; mode: Mode; onClose: () => void; onDeal: (dealId: string | null) => void;
}) {
  const calledIn = !!r.calledIn;
  const [offerId, setOfferId] = useState<string>(r.offers.length === 1 ? r.offers[0].id : '');
  const [custom, setCustom] = useState('');
  const [customIsChildcare, setCustomIsChildcare] = useState(false);
  const [loose, setLoose] = useState(false);
  const [time, setTime] = useState(toLocalInput(r.neededFrom));
  const [message, setMessage] = useState('');

  const submit = useAction(
    () => api<{ dealId: string | null }>(`/requests/${r.id}/respond`, {
      body: {
        kind: mode.kind,
        responderId: mode.responderId,
        selectedOfferId: offerId && offerId !== 'custom' && offerId !== 'loose' ? offerId : null,
        customText: offerId === 'custom' ? custom : null,
        customIsChildcare: offerId === 'custom' && customIsChildcare,
        loose: loose || offerId === 'loose',
        message,
        proposedTime: fromLocalInput(time),
      },
    }),
    {
      success: mode.kind === 'accept' ? 'Deal made' : 'Counter sent',
      onSuccess: (res) => (mode.kind === 'accept' ? onDeal(res.dealId) : onClose()),
    },
  );

  const needsOffer = !calledIn && mode.kind === 'accept' && r.offers.length > 1 && !offerId;
  const needsTerms = !calledIn && mode.kind === 'counter' && !offerId && !time && !message.trim();
  const title = calledIn
    ? (mode.kind === 'accept' ? 'Agree to this' : 'Suggest a different time')
    : (mode.kind === 'accept' ? 'Accept' : 'Counter');

  return (
    <Modal opened onClose={onClose} title={title} centered>
      <Stack gap="md">
        {!calledIn && (r.offers.length > 1 || mode.kind === 'counter') && (
          <Radio.Group label={mode.kind === 'accept' ? 'Which would you like?' : 'What would you take?'} value={offerId} onChange={setOfferId}>
            <Stack gap="xs" mt="xs">
              {r.offers.map((o) => <Radio key={o.id} value={o.id} label={o.description} />)}
              {mode.kind === 'counter' && (
                <>
                  <Radio value="custom" label="Something else" />
                  <Radio value="loose" label='Just "you owe me one"' />
                </>
              )}
            </Stack>
          </Radio.Group>
        )}
        {offerId === 'custom' && (
          <>
            <TextInput label="Your terms" placeholder="e.g. Two light fixtures instead of one" value={custom}
              onChange={(e) => setCustom(e.currentTarget.value)} maxLength={300} data-autofocus />
            <Checkbox label="This is childcare" checked={customIsChildcare}
              onChange={(e) => setCustomIsChildcare(e.currentTarget.checked)} />
          </>
        )}
        {!calledIn && mode.kind === 'accept' && (
          <Checkbox label={`Skip the offers: ${r.author.name} just owes me one`} checked={loose}
            onChange={(e) => setLoose(e.currentTarget.checked)} />
        )}
        <TextInput type="datetime-local" label="When" description={calledIn ? undefined : 'Optional'}
          value={time} onChange={(e) => setTime(e.currentTarget.value)} />
        <Textarea label="Message" placeholder="Optional" value={message} onChange={(e) => setMessage(e.currentTarget.value)} />
        <Button disabled={needsOffer || needsTerms || (offerId === 'custom' && !custom.trim())}
          loading={submit.isPending} onClick={() => submit.mutate()}>
          {mode.kind === 'accept' ? (calledIn ? 'Agree' : 'Accept and make the deal') : 'Send counter'}
        </Button>
      </Stack>
    </Modal>
  );
}
