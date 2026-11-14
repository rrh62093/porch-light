import { useState } from 'react';
import {
  ActionIcon, Badge, Button, Card, Group, Menu, Modal, Select, Stack, Switch, Text, TextInput, Textarea,
} from '@mantine/core';
import { IconBabyCarriage, IconDots, IconFlag } from '@tabler/icons-react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, type CircleDetail, type FlagRow } from '../api';
import { ago, useAction, useApi, useMe } from '../lib';
import { Loading, Page, SectionTitle } from '../components/Layout';

export function CircleDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const me = useMe();
  const q = useApi<CircleDetail>(['circle', id], `/circles/${id}`);
  const [flagging, setFlagging] = useState(false);
  const [renaming, setRenaming] = useState(false);
  const [confirmLeave, setConfirmLeave] = useState(false);

  const mute = useAction((muted: boolean) => api(`/circles/${id}/mute`, { method: 'PUT', body: { muted } }));
  const leave = useAction(() => api(`/circles/${id}/leave`, { method: 'POST' }), {
    success: 'You left the circle. Any IOUs stay open.',
    onSuccess: () => navigate('/circles', { replace: true }),
  });

  return (
    <Page title={q.data?.name ?? 'Circle'} back right={q.data && (
      <Menu position="bottom-end">
        <Menu.Target><ActionIcon variant="subtle" color="gray" size="lg" aria-label="More"><IconDots /></ActionIcon></Menu.Target>
        <Menu.Dropdown>
          {q.data.myRole === 'admin' && <Menu.Item onClick={() => setRenaming(true)}>Rename circle</Menu.Item>}
          <Menu.Item leftSection={<IconFlag size={16} />} onClick={() => setFlagging(true)}>Flag something to an admin</Menu.Item>
          <Menu.Item color="red" onClick={() => setConfirmLeave(true)}>Leave circle</Menu.Item>
        </Menu.Dropdown>
      </Menu>
    )}>
      <Loading query={q}>
        {() => {
          const c = q.data!;
          const isAdmin = c.myRole === 'admin';
          return (
            <Stack gap="sm">
              <Group grow>
                <Button component={Link} to={`/requests/new?circle=${c.id}`}>Ask this circle</Button>
              </Group>
              <Card p="sm">
                <Switch label="Mute notifications from this circle" checked={c.muted} color="amber"
                  onChange={(e) => mute.mutate(e.currentTarget.checked)} />
              </Card>

              {isAdmin && <InviteCard circle={c} />}
              {isAdmin && c.openFlags > 0 && <FlagsCard circleId={c.id} />}

              <SectionTitle>{c.members.length} {c.members.length === 1 ? 'member' : 'members'}</SectionTitle>
              {c.members.map((m) => (
                <Card key={m.id} p="sm">
                  <Group justify="space-between" wrap="nowrap">
                    <Link to={`/people/${m.id}`} className="pl-card-link" style={{ flex: 1, minWidth: 0 }}>
                      <Group gap={6}>
                        <Text fw={600}>{m.id === me.data?.user.id ? `${m.name} (you)` : m.name}</Text>
                        {m.role === 'admin' && <Badge size="xs" color="gray">Admin</Badge>}
                        {(m.offersChildcare || m.needsChildcare) && (
                          <Badge size="xs" color="gray" leftSection={<IconBabyCarriage size={10} />}>
                            {m.offersChildcare && m.needsChildcare ? 'Childcare swap' : m.offersChildcare ? 'Offers childcare' : 'Needs childcare'}
                          </Badge>
                        )}
                      </Group>
                      {m.skills.length > 0 && <Text size="sm" c="dimmed" lineClamp={1}>{m.skills.join(', ')}</Text>}
                    </Link>
                    {isAdmin && m.id !== me.data?.user.id && <MemberMenu circleId={c.id} member={m} />}
                  </Group>
                </Card>
              ))}
            </Stack>
          );
        }}
      </Loading>

      {flagging && q.data && <FlagModal circle={q.data} onClose={() => setFlagging(false)} />}
      {renaming && q.data && <RenameModal circle={q.data} onClose={() => setRenaming(false)} />}
      <Modal opened={confirmLeave} onClose={() => setConfirmLeave(false)} title="Leave this circle?" centered>
        <Text size="sm">You'll stop seeing its requests. Any IOUs you have with members stay open.</Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setConfirmLeave(false)}>Stay</Button>
          <Button color="red" loading={leave.isPending} onClick={() => leave.mutate()}>Leave</Button>
        </Group>
      </Modal>
    </Page>
  );
}

function MemberMenu({ circleId, member }: { circleId: string; member: CircleDetail['members'][number] }) {
  const [confirm, setConfirm] = useState(false);
  const promote = useAction(() => api(`/circles/${circleId}/members/${member.id}/promote`, { method: 'POST' }), { success: `${member.name} is now an admin` });
  const remove = useAction(() => api(`/circles/${circleId}/members/${member.id}`, { method: 'DELETE' }), { success: `${member.name} was removed` });
  return (
    <>
      <Menu position="bottom-end">
        <Menu.Target><ActionIcon variant="subtle" color="gray" aria-label={`Manage ${member.name}`}><IconDots size={18} /></ActionIcon></Menu.Target>
        <Menu.Dropdown>
          {member.role !== 'admin' && <Menu.Item onClick={() => promote.mutate()}>Make admin</Menu.Item>}
          <Menu.Item color="red" onClick={() => setConfirm(true)}>Remove from circle</Menu.Item>
        </Menu.Dropdown>
      </Menu>
      <Modal opened={confirm} onClose={() => setConfirm(false)} title={`Remove ${member.name}?`} centered>
        <Text size="sm">
          They'll lose access to this circle's requests. Open IOUs stay on the books, and anyone with a deal with them can
          choose to close them out.
        </Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setConfirm(false)}>Cancel</Button>
          <Button color="red" loading={remove.isPending} onClick={() => remove.mutate(undefined, { onSuccess: () => setConfirm(false) })}>Remove</Button>
        </Group>
      </Modal>
    </>
  );
}

function InviteCard({ circle }: { circle: CircleDetail }) {
  const [email, setEmail] = useState('');
  const invite = useAction(() => api(`/circles/${circle.id}/invites`, { body: { email } }), {
    success: 'Invite saved. Tell them to sign in with that Google account.',
    onSuccess: () => setEmail(''),
  });
  const revoke = useAction((inviteId: string) => api(`/circles/${circle.id}/invites/${inviteId}`, { method: 'DELETE' }));
  return (
    <Card>
      <Text fw={600} mb="xs">Invite a dad</Text>
      <Group gap="xs" wrap="nowrap" align="flex-end">
        <TextInput style={{ flex: 1 }} type="email" placeholder="Their Gmail address" value={email}
          onChange={(e) => setEmail(e.currentTarget.value)} />
        <Button disabled={!email.includes('@')} loading={invite.isPending} onClick={() => invite.mutate()}>Invite</Button>
      </Group>
      <Text size="xs" c="dimmed" mt={6}>
        During the pilot, also add them as a test user in Google Cloud so they can sign in.
      </Text>
      {circle.invites.length > 0 && (
        <Stack gap={4} mt="sm">
          {circle.invites.map((i) => (
            <Group key={i.id} justify="space-between" wrap="nowrap">
              <Text size="sm" truncate>{i.email} <Text span c="dimmed" size="xs">pending</Text></Text>
              <Button size="xs" variant="subtle" color="gray" onClick={() => revoke.mutate(i.id)}>Revoke</Button>
            </Group>
          ))}
        </Stack>
      )}
    </Card>
  );
}

function FlagsCard({ circleId }: { circleId: string }) {
  const flags = useApi<FlagRow[]>(['flags', circleId], `/circles/${circleId}/flags`);
  const resolve = useAction((flagId: string) => api(`/circles/${circleId}/flags/${flagId}/resolve`, { method: 'POST' }));
  const open = flags.data?.filter((f) => !f.resolved) ?? [];
  return (
    <Card className="pl-unread">
      <Text fw={600} mb="xs">Private flags</Text>
      <Stack gap="sm">
        {open.map((f) => (
          <div key={f.id}>
            <Text size="sm" c="dimmed">
              From {f.reportedBy.name}{f.about ? ` about ${f.about.name}` : ''} · {ago(f.createdAt)}
            </Text>
            <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{f.message}</Text>
            <Button size="xs" variant="subtle" color="gray" px={0} onClick={() => resolve.mutate(f.id)}>Mark handled</Button>
          </div>
        ))}
      </Stack>
    </Card>
  );
}

function FlagModal({ circle, onClose }: { circle: CircleDetail; onClose: () => void }) {
  const me = useMe();
  const [about, setAbout] = useState<string | null>(null);
  const [message, setMessage] = useState('');
  const send = useAction(() => api(`/circles/${circle.id}/flags`, { body: { aboutUserId: about, message } }), {
    success: 'Sent privately to the admins', onSuccess: onClose,
  });
  return (
    <Modal opened onClose={onClose} title="Flag something to an admin" centered>
      <Stack>
        <Text size="sm" c="dimmed">Only this circle's admins will see this.</Text>
        <Select label="About" placeholder="Optional" clearable
          data={circle.members.filter((m) => m.id !== me.data?.user.id).map((m) => ({ value: m.id, label: m.name }))}
          value={about} onChange={setAbout} />
        <Textarea label="What's going on?" minRows={3} value={message} onChange={(e) => setMessage(e.currentTarget.value)} />
        <Button disabled={!message.trim()} loading={send.isPending} onClick={() => send.mutate()}>Send privately</Button>
      </Stack>
    </Modal>
  );
}

function RenameModal({ circle, onClose }: { circle: CircleDetail; onClose: () => void }) {
  const [name, setName] = useState(circle.name);
  const save = useAction(() => api(`/circles/${circle.id}`, { method: 'PUT', body: { name } }), { onSuccess: onClose });
  return (
    <Modal opened onClose={onClose} title="Rename circle" centered>
      <Stack>
        <TextInput value={name} maxLength={80} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
        <Button disabled={!name.trim()} loading={save.isPending} onClick={() => save.mutate()}>Save</Button>
      </Stack>
    </Modal>
  );
}
