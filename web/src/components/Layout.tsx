import type { ReactNode } from 'react';
import { ActionIcon, Badge, Center, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { IconArrowLeft, IconArrowsExchange, IconCircles, IconInbox, IconPlus, IconReceipt } from '@tabler/icons-react';
import { NavLink, useNavigate } from 'react-router-dom';
import { useMe } from '../lib';
import type { ApiError } from '../api';

export function Logo({ size = 28 }: { size?: number }) {
  return <img src="/icon.svg" width={size} height={size} alt="" style={{ borderRadius: size * 0.22, display: 'block' }} />;
}

export function Page({ title, back, right, children }: {
  title: ReactNode; back?: boolean; right?: ReactNode; children: ReactNode;
}) {
  const navigate = useNavigate();
  return (
    <>
      <header className="pl-header">
        <Group justify="space-between" wrap="nowrap" maw={640} mx="auto">
          <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
            {back ? (
              <ActionIcon variant="subtle" color="gray" size="lg" aria-label="Back"
                onClick={() => (window.history.length > 1 ? navigate(-1) : navigate('/'))}>
                <IconArrowLeft size={22} />
              </ActionIcon>
            ) : (
              <Logo />
            )}
            <Title order={1} size="h3" lineClamp={1}>{title}</Title>
          </Group>
          {right}
        </Group>
      </header>
      <main className="pl-main">{children}</main>
    </>
  );
}

/** Loading and error states for a page's main query. */
export function Loading({ query, children }: { query: { isLoading: boolean; error: ApiError | null }; children: () => ReactNode }) {
  if (query.isLoading) return <Center py="xl"><Loader color="amber" /></Center>;
  if (query.error) return <Text c="red" ta="center" py="xl">{query.error.message}</Text>;
  return <>{children()}</>;
}

export function Empty({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <Stack align="center" gap={6} py={40} px="md" ta="center">
      <Text fw={600}>{title}</Text>
      {children && <Text c="dimmed" size="sm">{children}</Text>}
    </Stack>
  );
}

export function SectionTitle({ children }: { children: ReactNode }) {
  return <div className="pl-section-title">{children}</div>;
}

function Count({ n }: { n: number }) {
  if (!n) return null;
  return <Badge className="pl-count" size="xs" variant="filled" color="amber" circle>{n}</Badge>;
}

export function TabBar() {
  const me = useMe();
  const c = me.data?.counts;
  return (
    <nav className="pl-tabbar" aria-label="Main">
      <NavLink to="/requests" className="pl-tab">
        <IconInbox size={24} stroke={1.6} />
        Requests
        <Count n={c?.pendingInvites ?? 0} />
      </NavLink>
      <NavLink to="/owe" className="pl-tab">
        <IconReceipt size={24} stroke={1.6} />
        I Owe
        <Count n={c?.iOwe ?? 0} />
      </NavLink>
      <NavLink to="/requests/new" className="pl-post" aria-label="Post a request">
        <IconPlus size={28} stroke={2.2} />
      </NavLink>
      <NavLink to="/owed" className="pl-tab">
        <IconArrowsExchange size={24} stroke={1.6} />
        Owed to Me
        <Count n={c?.owedToMe ?? 0} />
      </NavLink>
      <NavLink to="/circles" className="pl-tab">
        <IconCircles size={24} stroke={1.6} />
        Circles
      </NavLink>
    </nav>
  );
}
