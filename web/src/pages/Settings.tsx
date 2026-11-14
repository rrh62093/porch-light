import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Group, List, Stack, Switch, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useQueryClient } from '@tanstack/react-query';
import { api, type AuthConfig, type NotificationPrefs } from '../api';
import { useAction, useApi, useMe } from '../lib';
import { Page, SectionTitle } from '../components/Layout';
import { disablePush, enablePush, getPushState, isIos, type PushState } from '../push';

export function SettingsPage() {
  const me = useMe();
  const qc = useQueryClient();
  const config = useApi<AuthConfig>(['authConfig'], '/auth/config');
  const prefs = me.data!.user.notifications;
  const savePrefs = useAction((p: NotificationPrefs) => api('/me/notifications', { method: 'PUT', body: p }));
  const toggle = (key: keyof NotificationPrefs) => (e: React.ChangeEvent<HTMLInputElement>) =>
    savePrefs.mutate({ ...prefs, [key]: e.currentTarget.checked });

  const signOut = async () => {
    await api('/auth/logout', { method: 'POST' });
    qc.clear();
    window.location.href = '/';
  };

  return (
    <Page title="Settings" back>
      <Stack gap="sm">
        <PushCard vapidKey={config.data?.vapidPublicKey ?? null} loaded={!!config.data} />

        <SectionTitle>Notify me about</SectionTitle>
        <Card>
          <Stack gap="md">
            <Switch color="amber" label="New requests in my circles" description="Can be chatty. Turn off to only hear about things involving you."
              checked={prefs.newRequests} onChange={toggle('newRequests')} />
            <Switch color="amber" label="Responses to my requests" description="Offers and counters"
              checked={prefs.responses} onChange={toggle('responses')} />
            <Switch color="amber" label="Deal updates" description="Accepted, completed, IOU called in"
              checked={prefs.dealUpdates} onChange={toggle('dealUpdates')} />
            <Switch color="amber" label="Reminders" description="Before a scheduled favor"
              checked={prefs.reminders} onChange={toggle('reminders')} />
          </Stack>
        </Card>
        <Text size="xs" c="dimmed">You can also mute a whole circle from its page, or one person from their profile.</Text>

        <Button variant="default" color="red" mt="lg" onClick={signOut}>Sign out</Button>
      </Stack>
    </Page>
  );
}

function PushCard({ vapidKey, loaded }: { vapidKey: string | null; loaded: boolean }) {
  const [state, setState] = useState<PushState | null>(null);
  const [busy, setBusy] = useState(false);
  const refresh = useCallback(() => { getPushState(vapidKey).then(setState).catch(() => setState('unsupported')); }, [vapidKey]);
  useEffect(() => { if (loaded) refresh(); }, [loaded, refresh]);

  const run = async (fn: () => Promise<unknown>, ok?: string) => {
    setBusy(true);
    try {
      await fn();
      if (ok) notifications.show({ message: ok, color: 'teal' });
    } catch (e) {
      notifications.show({ message: (e as Error).message, color: 'red' });
    } finally {
      setBusy(false);
      refresh();
    }
  };

  if (!state) return null;
  return (
    <>
      <SectionTitle>Notifications on this device</SectionTitle>
      <Card>
        {state === 'on' && (
          <Stack gap="xs">
            <Text>Notifications are on for this device.</Text>
            <Group gap="xs">
              <Button size="sm" variant="default" loading={busy} onClick={() => run(() => api('/push/test', { method: 'POST' }), 'Test sent')}>
                Send a test
              </Button>
              <Button size="sm" variant="subtle" color="gray" loading={busy} onClick={() => run(disablePush)}>Turn off</Button>
            </Group>
          </Stack>
        )}
        {state === 'off' && (
          <Stack gap="xs">
            <Text>Get a buzz when someone answers your request or calls in an IOU.</Text>
            <Button loading={busy} onClick={() => run(() => enablePush(vapidKey!), 'Notifications on')}>Turn on notifications</Button>
          </Stack>
        )}
        {state === 'needs-install' && <InstallGuide />}
        {state === 'denied' && (
          <Alert color="gray">
            Notifications are blocked for Porch Light. {isIos()
              ? 'Open the iPhone Settings app, go to Notifications, find Porch Light, and allow them.'
              : 'Allow them in your browser\'s site settings, then come back here.'}
          </Alert>
        )}
        {state === 'unsupported' && <Text c="dimmed">This browser doesn't support notifications.</Text>}
        {state === 'not-configured' && <Text c="dimmed">Push isn't set up on the server yet (VAPID keys). See the README.</Text>}
      </Card>
    </>
  );
}

function InstallGuide() {
  return (
    <Stack gap="xs">
      <Text fw={600}>On iPhone, add Porch Light to your home screen first</Text>
      <List type="ordered" size="sm" spacing={4}>
        <List.Item>Open this page in Safari.</List.Item>
        <List.Item>Tap the Share button (the square with the arrow).</List.Item>
        <List.Item>Choose <b>Add to Home Screen</b>, then <b>Add</b>.</List.Item>
        <List.Item>Open Porch Light from your home screen, come back here, and tap <b>Turn on notifications</b>.</List.Item>
      </List>
    </Stack>
  );
}
