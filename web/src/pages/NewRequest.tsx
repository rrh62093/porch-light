import { useEffect, useState } from 'react';
import {
  ActionIcon, Button, Checkbox, Chip, Group, SegmentedControl, Select, Stack, Switch, Text, TextInput, Textarea,
} from '@mantine/core';
import { IconTrash } from '@tabler/icons-react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, type CircleSummary, type Ref } from '../api';
import { fromLocalInput, useAction, useApi } from '../lib';
import { Empty, Loading, Page, SectionTitle } from '../components/Layout';

interface OfferDraft { description: string; isChildcare: boolean }

const SUGGESTIONS: OfferDraft[] = [
  { description: 'Watch your kids for an evening', isChildcare: true },
  { description: 'Beer on me', isChildcare: false },
  { description: 'Help with a project of yours', isChildcare: false },
];

export function NewRequestPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const circles = useApi<CircleSummary[]>(['circles'], '/circles');
  const people = useApi<Ref[]>(['people'], '/people');

  const [audience, setAudience] = useState<'circle' | 'direct'>(params.get('to') ? 'direct' : 'circle');
  const [circleId, setCircleId] = useState<string | null>(params.get('circle'));
  const [recipientId, setRecipientId] = useState<string | null>(params.get('to'));
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [isChildcare, setIsChildcare] = useState(false);
  const [childcareNote, setChildcareNote] = useState('');
  const [offers, setOffers] = useState<OfferDraft[]>([{ description: '', isChildcare: false }]);
  const [rate, setRate] = useState('');

  useEffect(() => {
    if (!circleId && circles.data?.length) setCircleId(circles.data[0].id);
  }, [circles.data, circleId]);

  const post = useAction(
    () => api<{ id: string }>('/requests', {
      body: {
        circleId: audience === 'circle' ? circleId : null,
        recipientId: audience === 'direct' ? recipientId : null,
        title, description, isChildcare, childcareNote,
        neededFrom: fromLocalInput(from), neededTo: fromLocalInput(to),
        suggestedRate: rate,
        offers: offers.filter((o) => o.description.trim()),
      },
    }),
    { success: 'Posted', onSuccess: (r) => navigate(`/requests/${r.id}`, { replace: true }) },
  );

  const setOffer = (i: number, patch: Partial<OfferDraft>) =>
    setOffers((os) => os.map((o, j) => (j === i ? { ...o, ...patch } : o)));
  const addSuggestion = (s: OfferDraft) =>
    setOffers((os) => [...os.filter((o) => o.description.trim()), s]);

  const ready = title.trim() && offers.some((o) => o.description.trim()) &&
    (audience === 'circle' ? circleId : recipientId);

  return (
    <Page title="Post a request" back>
      <Loading query={circles}>
        {() => circles.data!.length === 0 ? (
          <Empty title="Join or start a circle first">Requests go to your circles of friends.</Empty>
        ) : (
          <Stack gap="md">
            <SegmentedControl
              fullWidth
              value={audience}
              onChange={(v) => setAudience(v as 'circle' | 'direct')}
              data={[{ value: 'circle', label: 'Ask a circle' }, { value: 'direct', label: 'Ask one dad' }]}
            />
            {audience === 'circle' ? (
              <Select label="Circle" data={circles.data!.map((c) => ({ value: c.id, label: c.name }))}
                value={circleId} onChange={setCircleId} allowDeselect={false} />
            ) : (
              <Select label="Who" placeholder="Pick a dad" searchable
                data={(people.data ?? []).map((p) => ({ value: p.id, label: p.name }))}
                value={recipientId} onChange={setRecipientId} />
            )}

            <TextInput label="What do you need?" placeholder="e.g. Borrow a 16 ft+ ladder" value={title}
              onChange={(e) => setTitle(e.currentTarget.value)} maxLength={140} />
            <Textarea label="Details" placeholder="Optional" value={description}
              onChange={(e) => setDescription(e.currentTarget.value)} />

            <Group grow align="flex-start">
              <TextInput type="datetime-local" label="From" description="Optional" value={from}
                onChange={(e) => setFrom(e.currentTarget.value)} />
              <TextInput type="datetime-local" label="To" description="Optional" value={to}
                onChange={(e) => setTo(e.currentTarget.value)} />
            </Group>

            <Switch label="This is for watching my kids" checked={isChildcare}
              onChange={(e) => setIsChildcare(e.currentTarget.checked)} color="amber" />
            {isChildcare && (
              <>
                <Text size="sm" c="dimmed">
                  Your kids' ages, allergies, and contact number from your profile go only to the dad who takes this on.
                </Text>
                <Textarea label="Note for this time" placeholder="e.g. Bedtime is 8, pizza in the freezer"
                  value={childcareNote} onChange={(e) => setChildcareNote(e.currentTarget.value)} />
              </>
            )}

            <div>
              <SectionTitle>What you'd give in exchange</SectionTitle>
              <Text size="sm" c="dimmed" mb="xs">List a few options. They can pick one or counter.</Text>
              <Stack gap="xs">
                {offers.map((o, i) => (
                  <Stack key={i} gap={4}>
                    <Group gap="xs" wrap="nowrap">
                      <TextInput style={{ flex: 1 }} placeholder="e.g. Hang a light fixture" value={o.description}
                        onChange={(e) => setOffer(i, { description: e.currentTarget.value })} maxLength={200} />
                      {offers.length > 1 && (
                        <ActionIcon variant="subtle" color="gray" size="lg" aria-label="Remove offer"
                          onClick={() => setOffers((os) => os.filter((_, j) => j !== i))}>
                          <IconTrash size={18} />
                        </ActionIcon>
                      )}
                    </Group>
                    <Checkbox size="xs" label="This offer is childcare" checked={o.isChildcare}
                      onChange={(e) => setOffer(i, { isChildcare: e.currentTarget.checked })} />
                  </Stack>
                ))}
                <Group gap={6}>
                  <Button variant="light" color="gray" size="xs"
                    onClick={() => setOffers((os) => [...os, { description: '', isChildcare: false }])}>
                    Add another
                  </Button>
                  {SUGGESTIONS.filter((s) => !offers.some((o) => o.description === s.description)).map((s) => (
                    <Chip key={s.description} size="xs" checked={false} onChange={() => addSuggestion(s)}>
                      {s.description}
                    </Chip>
                  ))}
                </Group>
              </Stack>
            </div>

            <TextInput label="Suggested rate" description="Optional, e.g. about $20/hr" value={rate}
              onChange={(e) => setRate(e.currentTarget.value)} maxLength={60} />

            <Button size="lg" disabled={!ready} loading={post.isPending} onClick={() => post.mutate()}>
              Post request
            </Button>
          </Stack>
        )}
      </Loading>
    </Page>
  );
}
