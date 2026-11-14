import { useEffect, useState } from 'react';
import {
  ActionIcon, Button, Card, Group, Menu, Modal, SegmentedControl, Stack, Switch, Text, TextInput, Textarea,
} from '@mantine/core';
import { IconDots, IconLock } from '@tabler/icons-react';
import { api, type ChildProfile, type ItemKind, type Person, type ProfileItem } from '../api';
import { useAction, useApi, useMe } from '../lib';
import { Loading, Page, SectionTitle } from '../components/Layout';
import { ItemSections } from '../components/Items';

export function MyProfilePage() {
  const me = useMe();
  const myId = me.data!.user.id;
  const profile = useApi<Person>(['person', myId], `/people/${myId}`);
  const [editing, setEditing] = useState<ProfileItem | 'new' | null>(null);
  const [name, setName] = useState(me.data!.user.name);
  const saveName = useAction(() => api('/me', { method: 'PUT', body: { name } }), { success: 'Saved' });
  const remove = useAction((itemId: string) => api(`/me/items/${itemId}`, { method: 'DELETE' }));

  return (
    <Page title="My profile" back>
      <Stack gap="sm">
        <Card>
          <Group gap="xs" align="flex-end" wrap="nowrap">
            <TextInput style={{ flex: 1 }} label="Name" value={name} maxLength={100} onChange={(e) => setName(e.currentTarget.value)} />
            <Button variant="default" disabled={!name.trim() || name === me.data!.user.name}
              loading={saveName.isPending} onClick={() => saveName.mutate()}>Save</Button>
          </Group>
          <Text size="sm" c="dimmed" mt={6}>{me.data!.user.email}</Text>
        </Card>

        <Group justify="space-between" mt="sm">
          <Text fw={650}>Skills, tools, and wants</Text>
          <Button size="xs" variant="light" onClick={() => setEditing('new')}>Add</Button>
        </Group>
        <Loading query={profile}>
          {() => profile.data!.items.length === 0 ? (
            <Text c="dimmed" size="sm">
              Add what you're good at (electrical, smoking a brisket), tools you'd lend (16 ft ladder, pressure washer),
              and things you're looking for.
            </Text>
          ) : (
            <ItemSections items={profile.data!.items} canEndorse={false} renderActions={(i) => (
              <Menu position="bottom-end">
                <Menu.Target><ActionIcon variant="subtle" color="gray" aria-label={`Edit ${i.name}`}><IconDots size={18} /></ActionIcon></Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item onClick={() => setEditing(i)}>Edit</Menu.Item>
                  <Menu.Item color="red" onClick={() => remove.mutate(i.id)}>Delete</Menu.Item>
                </Menu.Dropdown>
              </Menu>
            )} />
          )}
        </Loading>

        <ChildcareSection />
      </Stack>
      {editing && <ItemModal item={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </Page>
  );
}

function ItemModal({ item, onClose }: { item: ProfileItem | null; onClose: () => void }) {
  const [kind, setKind] = useState<ItemKind>(item?.kind ?? 'skill');
  const [name, setName] = useState(item?.name ?? '');
  const [description, setDescription] = useState(item?.description ?? '');
  const [available, setAvailable] = useState(item?.availableForTrade ?? true);
  const body = { kind, name, description, availableForTrade: available };
  const save = useAction(
    () => item ? api(`/me/items/${item.id}`, { method: 'PUT', body }) : api('/me/items', { body }),
    { onSuccess: onClose },
  );
  return (
    <Modal opened onClose={onClose} title={item ? 'Edit' : 'Add to your profile'} centered>
      <Stack>
        <SegmentedControl fullWidth value={kind} onChange={(v) => setKind(v as ItemKind)}
          data={[{ value: 'skill', label: 'Skill' }, { value: 'tool', label: 'Tool' }, { value: 'lookingFor', label: 'Looking for' }]} />
        <TextInput label="Name" data-autofocus maxLength={100} value={name} onChange={(e) => setName(e.currentTarget.value)}
          placeholder={kind === 'skill' ? 'e.g. Electrical' : kind === 'tool' ? 'e.g. 24 ft extension ladder' : 'e.g. Someone to help with drywall'} />
        <Textarea label="Details" placeholder="Optional" value={description} onChange={(e) => setDescription(e.currentTarget.value)} />
        {kind !== 'lookingFor' && (
          <Switch label="Available to trade" color="amber" checked={available} onChange={(e) => setAvailable(e.currentTarget.checked)} />
        )}
        <Button disabled={!name.trim()} loading={save.isPending} onClick={() => save.mutate()}>Save</Button>
      </Stack>
    </Modal>
  );
}

function ChildcareSection() {
  const q = useApi<ChildProfile>(['childProfile'], '/me/child-profile');
  const [form, setForm] = useState<ChildProfile | null>(null);
  useEffect(() => { if (q.data) setForm(q.data); }, [q.data]);
  const save = useAction(() => api('/me/child-profile', { method: 'PUT', body: form }), { success: 'Saved' });
  const set = (patch: Partial<ChildProfile>) => setForm((f) => ({ ...f!, ...patch }));

  return (
    <>
      <SectionTitle>Childcare</SectionTitle>
      <Loading query={q}>
        {() => form && (
          <Card>
            <Stack gap="sm">
              <Switch label="I'm happy to watch other dads' kids" color="amber" checked={form.offersChildcare}
                onChange={(e) => set({ offersChildcare: e.currentTarget.checked })} />
              <Switch label="I sometimes need someone to watch mine" color="amber" checked={form.needsChildcare}
                onChange={(e) => set({ needsChildcare: e.currentTarget.checked })} />
              <Text size="xs" c="dimmed">Your circles see only these two switches.</Text>

              <Group gap={6} mt="xs">
                <IconLock size={16} />
                <Text size="sm" fw={600}>Kid details</Text>
              </Group>
              <Text size="xs" c="dimmed" mt={-8}>
                Shared only with the dad on an accepted childcare deal, and only while it's open.
              </Text>
              <TextInput label="Kids' ages" placeholder="e.g. 4 and 7" value={form.kidsAges ?? ''}
                onChange={(e) => set({ kidsAges: e.currentTarget.value })} />
              <Textarea label="Allergies" placeholder="e.g. Peanuts (EpiPen in the blue bag)" value={form.allergies ?? ''}
                onChange={(e) => set({ allergies: e.currentTarget.value })} />
              <TextInput label="Contact number" type="tel" value={form.contactNumber ?? ''}
                onChange={(e) => set({ contactNumber: e.currentTarget.value })} />
              <Textarea label="Notes" placeholder="Bedtimes, screen rules, anything useful" value={form.notes ?? ''}
                onChange={(e) => set({ notes: e.currentTarget.value })} />
              <Button loading={save.isPending} onClick={() => save.mutate()}>Save childcare info</Button>
            </Stack>
          </Card>
        )}
      </Loading>
    </>
  );
}
