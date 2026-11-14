import { useState } from 'react';
import { Badge, Card, Group, SegmentedControl, Stack, Text } from '@mantine/core';
import { Link } from 'react-router-dom';
import type { Ledger, LedgerRow } from '../api';
import { dateOnly, legStatusLabel, useApi, when } from '../lib';
import { Empty, Loading, Page, SectionTitle } from '../components/Layout';

/** Tabs 2 and 3: favors I owe, and favors owed to me. */
export function LedgerPage({ side }: { side: 'iOwe' | 'owedToMe' }) {
  const [view, setView] = useState('open');
  const history = view === 'history';
  const q = useApi<Ledger>(['ledger', history], `/ledger${history ? '?history=true' : ''}`);
  const iOwe = side === 'iOwe';

  return (
    <Page title={iOwe ? 'I Owe' : 'Owed to Me'}>
      <Stack gap="sm">
        <SegmentedControl fullWidth value={view} onChange={setView}
          data={[{ value: 'open', label: 'Open' }, { value: 'history', label: 'History' }]} />
        <Loading query={q}>
          {() => {
            const rows = q.data![side];
            if (rows.length === 0) {
              return history ? <Empty title="Nothing here yet" /> : iOwe ? (
                <Empty title="You're all square">Favors you owe show up here once a deal is made.</Empty>
              ) : (
                <Empty title="Nobody owes you yet">When a friend owes you one, it lands here so you can call it in.</Empty>
              );
            }
            if (history) return rows.map((r) => <LegCard key={r.id} row={r} />);

            // What needs a tap from me first.
            const attention = rows.filter((r) => iOwe ? r.status === 'calledIn' : r.status === 'awaitingConfirmation');
            const rest = rows.filter((r) => !attention.includes(r));
            return (
              <>
                {attention.length > 0 && (
                  <>
                    <SectionTitle>{iOwe ? 'Called in' : 'Confirm these'}</SectionTitle>
                    {attention.map((r) => <LegCard key={r.id} row={r} highlight />)}
                  </>
                )}
                {rest.length > 0 && attention.length > 0 && <SectionTitle>Open</SectionTitle>}
                {rest.map((r) => <LegCard key={r.id} row={r} />)}
              </>
            );
          }}
        </Loading>
      </Stack>
    </Page>
  );
}

function LegCard({ row, highlight }: { row: LedgerRow; highlight?: boolean }) {
  const status = legStatusLabel[row.status];
  return (
    <Link to={`/deals/${row.dealId}`} className="pl-card-link">
      <Card className={highlight ? 'pl-unread' : undefined}>
        <Group justify="space-between" align="flex-start" wrap="nowrap" gap="xs">
          <div style={{ minWidth: 0 }}>
            <Text fw={600} lineClamp={2}>{row.description}</Text>
            <Text size="sm" c="dimmed">
              {row.iOwe ? `to ${row.counterparty.name}` : `from ${row.counterparty.name}`}
              {row.description !== row.dealTitle && ` · for "${row.dealTitle}"`}
            </Text>
          </div>
          <Badge color={status.color} style={{ flexShrink: 0 }}>{status.label}</Badge>
        </Group>
        <Group gap={6} mt={6}>
          {row.kind === 'loose' && <Badge color="gray">Loose IOU</Badge>}
          {row.scheduledAt && row.status !== 'completed' && <Text size="sm">{when(row.scheduledAt)}</Text>}
          {row.completedOn && (row.status === 'completed' || row.status === 'closedOut') && (
            <Text size="sm" c="dimmed">{dateOnly(row.completedOn)}</Text>
          )}
        </Group>
      </Card>
    </Link>
  );
}
