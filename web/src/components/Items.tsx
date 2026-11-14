import { Badge, Button, Card, Group, Stack, Text, Tooltip } from '@mantine/core';
import { IconStar, IconStarFilled } from '@tabler/icons-react';
import { api, type ItemKind, type ProfileItem } from '../api';
import { useAction } from '../lib';
import { SectionTitle } from './Layout';

export const KIND_LABELS: Record<ItemKind, string> = { skill: 'Skills', tool: 'Tools', lookingFor: 'Looking for' };

/** Skills, tools, and wants grouped by kind, with endorsements on skills and tools. */
export function ItemSections({ items, canEndorse, renderActions }: {
  items: ProfileItem[];
  canEndorse: boolean;
  renderActions?: (item: ProfileItem) => React.ReactNode;
}) {
  return (
    <>
      {(['skill', 'tool', 'lookingFor'] as ItemKind[]).map((kind) => {
        const list = items.filter((i) => i.kind === kind);
        if (list.length === 0) return null;
        return (
          <div key={kind}>
            <SectionTitle>{KIND_LABELS[kind]}</SectionTitle>
            <Stack gap="xs">
              {list.map((i) => <ItemRow key={i.id} item={i} canEndorse={canEndorse && kind !== 'lookingFor'} actions={renderActions?.(i)} />)}
            </Stack>
          </div>
        );
      })}
    </>
  );
}

function ItemRow({ item, canEndorse, actions }: { item: ProfileItem; canEndorse: boolean; actions?: React.ReactNode }) {
  const toggle = useAction(() => api(`/items/${item.id}/endorse`, { method: item.endorsedByMe ? 'DELETE' : 'POST' }));
  return (
    <Card p="sm">
      <Group justify="space-between" wrap="nowrap" align="flex-start">
        <div style={{ minWidth: 0 }}>
          <Group gap={6}>
            <Text fw={600}>{item.name}</Text>
            {!item.availableForTrade && <Badge size="xs" color="gray">Not trading right now</Badge>}
          </Group>
          {item.description && <Text size="sm" c="dimmed">{item.description}</Text>}
          {item.kind !== 'lookingFor' && item.endorsementCount > 0 && (
            <Tooltip label={item.endorsers.join(', ')} events={{ hover: true, focus: true, touch: true }}>
              <Text size="xs" c="dimmed" mt={4}>
                Endorsed by {item.endorsementCount} {item.endorsementCount === 1 ? 'friend' : 'friends'}
              </Text>
            </Tooltip>
          )}
        </div>
        {canEndorse && (
          <Button size="xs" variant={item.endorsedByMe ? 'light' : 'default'} loading={toggle.isPending}
            leftSection={item.endorsedByMe ? <IconStarFilled size={14} /> : <IconStar size={14} />}
            onClick={() => toggle.mutate()}>
            {item.endorsedByMe ? 'Endorsed' : 'Endorse'}
          </Button>
        )}
        {actions}
      </Group>
    </Card>
  );
}
