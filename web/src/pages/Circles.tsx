import { useState } from 'react';
import { Badge, Button, Card, Group, Modal, Stack, Text, TextInput, UnstyledButton } from '@mantine/core';
import { IconBellOff, IconChevronRight, IconSettings, IconUser } from '@tabler/icons-react';
import { Link, useNavigate } from 'react-router-dom';
import { api, type CircleSummary } from '../api';
import { useAction, useApi, useMe } from '../lib';
import { Empty, Loading, Page, SectionTitle } from '../components/Layout';

export function CirclesPage() {
  const me = useMe();
  const circles = useApi<CircleSummary[]>(['circles'], '/circles');
  const [creating, setCreating] = useState(false);

  return (
    <Page title="Circles" right={<Button size="xs" variant="light" onClick={() => setCreating(true)}>New circle</Button>}>
      <Stack gap="sm">
        <Card p={0}>
          <RowLink to="/me" icon={<IconUser size={20} />} label="My profile" hint={me.data?.user.name} />
          <RowLink to="/settings" icon={<IconSettings size={20} />} label="Notifications & settings" border />
        </Card>

        <SectionTitle>My circles</SectionTitle>
        <Loading query={circles}>
          {() => circles.data!.length === 0 ? (
            <Empty title="You're not in a circle yet">
              Start one for your neighborhood dads or work friends, then invite them by email.
              <br />
              <Button mt="md" onClick={() => setCreating(true)}>Start a circle</Button>
            </Empty>
          ) : circles.data!.map((c) => (
            <Link key={c.id} to={`/circles/${c.id}`} className="pl-card-link">
              <Card>
                <Group justify="space-between" wrap="nowrap">
                  <div>
                    <Group gap={6}>
                      <Text fw={600}>{c.name}</Text>
                      {c.role === 'admin' && <Badge color="gray" size="xs">Admin</Badge>}
                      {c.muted && <IconBellOff size={14} color="var(--pl-muted)" aria-label="Muted" />}
                    </Group>
                    <Text size="sm" c="dimmed">
                      {c.memberCount} {c.memberCount === 1 ? 'dad' : 'dads'}
                      {c.openRequests > 0 && ` · ${c.openRequests} open ${c.openRequests === 1 ? 'request' : 'requests'}`}
                    </Text>
                  </div>
                  <IconChevronRight size={18} color="var(--pl-muted)" />
                </Group>
              </Card>
            </Link>
          ))}
        </Loading>
      </Stack>
      {creating && <CreateCircle onClose={() => setCreating(false)} />}
    </Page>
  );
}

function RowLink({ to, icon, label, hint, border }: { to: string; icon: React.ReactNode; label: string; hint?: string; border?: boolean }) {
  return (
    <UnstyledButton component={Link} to={to} w="100%" p="md"
      style={border ? { borderTop: '1px solid var(--pl-border)' } : undefined}>
      <Group justify="space-between" wrap="nowrap">
        <Group gap="sm">{icon}<Text fw={500}>{label}</Text></Group>
        <Group gap={4}>{hint && <Text size="sm" c="dimmed">{hint}</Text>}<IconChevronRight size={18} color="var(--pl-muted)" /></Group>
      </Group>
    </UnstyledButton>
  );
}

function CreateCircle({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const create = useAction(() => api<{ id: string }>('/circles', { body: { name } }), {
    success: 'Circle created. Invite some friends.',
    onSuccess: (r) => navigate(`/circles/${r.id}`),
  });
  return (
    <Modal opened onClose={onClose} title="New circle" centered>
      <Stack>
        <TextInput label="Name" placeholder="e.g. Maple Street dads" value={name} maxLength={80} data-autofocus
          onChange={(e) => setName(e.currentTarget.value)} />
        <Text size="sm" c="dimmed">You'll be the admin. Only admins can invite people.</Text>
        <Button disabled={!name.trim()} loading={create.isPending} onClick={() => create.mutate()}>Create</Button>
      </Stack>
    </Modal>
  );
}
