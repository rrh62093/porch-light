import { Badge, Button, Card, Group, Stack, Switch, Text } from '@mantine/core';
import { IconBabyCarriage } from '@tabler/icons-react';
import { Link, Navigate, useParams } from 'react-router-dom';
import { api, type Person } from '../api';
import { useAction, useApi } from '../lib';
import { Empty, Loading, Page, SectionTitle } from '../components/Layout';
import { ItemSections } from '../components/Items';

export function PersonPage() {
  const { id } = useParams();
  const q = useApi<Person>(['person', id], `/people/${id}`);
  const mute = useAction((muted: boolean) => api(`/people/${id}/mute`, { method: 'PUT', body: { muted } }));

  if (q.data?.isMe) return <Navigate to="/me" replace />;

  return (
    <Page title={q.data?.name ?? 'Profile'} back>
      <Loading query={q}>
        {() => {
          const p = q.data!;
          return (
            <Stack gap="sm">
              <Card>
                <Text fw={650} size="lg">{p.name}</Text>
                {p.email && <Text size="sm" c="dimmed">{p.email}</Text>}
                {p.sharedCircles.length > 0 && (
                  <Text size="sm" c="dimmed" mt={4}>
                    In {p.sharedCircles.map((c, i) => (
                      <span key={c.id}>{i > 0 && ', '}<Link to={`/circles/${c.id}`}>{c.name}</Link></span>
                    ))}
                  </Text>
                )}
                {(p.offersChildcare || p.needsChildcare) && (
                  <Group gap={6} mt="sm">
                    {p.offersChildcare && <Badge color="gray" leftSection={<IconBabyCarriage size={12} />}>Offers childcare</Badge>}
                    {p.needsChildcare && <Badge color="gray" leftSection={<IconBabyCarriage size={12} />}>Sometimes needs childcare</Badge>}
                  </Group>
                )}
                <Button component={Link} to={`/requests/new?to=${p.id}`} mt="md" fullWidth>Ask {p.name} directly</Button>
              </Card>

              {p.items.length === 0
                ? <Empty title="Nothing listed yet">{p.name} hasn't added skills or tools.</Empty>
                : <ItemSections items={p.items} canEndorse />}

              {p.childProfile && (
                <>
                  <SectionTitle>Kids (shared for your open deal)</SectionTitle>
                  <Card>
                    <Stack gap={4}>
                      {p.childProfile.kidsAges && <Text size="sm"><Text span c="dimmed">Ages: </Text>{p.childProfile.kidsAges}</Text>}
                      {p.childProfile.allergies && <Text size="sm"><Text span c="dimmed">Allergies: </Text>{p.childProfile.allergies}</Text>}
                      {p.childProfile.contactNumber && <Text size="sm"><Text span c="dimmed">Contact: </Text>{p.childProfile.contactNumber}</Text>}
                      {p.childProfile.notes && <Text size="sm"><Text span c="dimmed">Notes: </Text>{p.childProfile.notes}</Text>}
                    </Stack>
                  </Card>
                </>
              )}

              <Card p="sm" mt="md">
                <Switch label={`Mute notifications from ${p.name}`} color="amber" checked={p.mutedByMe}
                  description="Private. Their requests still show in your feed."
                  onChange={(e) => mute.mutate(e.currentTarget.checked)} />
              </Card>
            </Stack>
          );
        }}
      </Loading>
    </Page>
  );
}
