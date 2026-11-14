import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Center, Divider, Stack, Text, TextInput, Title } from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { api, ApiError, type AuthConfig } from '../api';
import { useApi } from '../lib';
import { Logo } from '../components/Layout';

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (opts: { client_id: string; callback: (r: { credential: string }) => void }) => void;
          renderButton: (el: HTMLElement, opts: Record<string, unknown>) => void;
        };
      };
    };
  }
}

// Load Google's sign-in script once per page load, however many times the login page mounts.
let googleScript: Promise<void> | null = null;
let googleInitializedFor: string | null = null;
function loadGoogle(): Promise<void> {
  if (window.google) return Promise.resolve();
  googleScript ??= new Promise<void>((resolve, reject) => {
    const s = document.createElement('script');
    s.src = 'https://accounts.google.com/gsi/client';
    s.async = true;
    s.onload = () => resolve();
    s.onerror = () => { googleScript = null; s.remove(); reject(new Error('Google sign-in failed to load')); };
    document.head.appendChild(s);
  });
  return googleScript;
}

export function LoginPage() {
  const config = useApi<AuthConfig>(['authConfig'], '/auth/config');
  const qc = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // A state-backed ref, so the effect re-runs whenever the button's container mounts.
  const [googleEl, setGoogleEl] = useState<HTMLDivElement | null>(null);

  const finish = async (call: Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await call;
      await qc.invalidateQueries();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Sign-in failed.');
    } finally {
      setBusy(false);
    }
  };

  // Google calls back into whichever render of this page is current.
  const finishRef = useRef(finish);
  finishRef.current = finish;

  const clientId = config.data?.googleClientId;
  useEffect(() => {
    if (!clientId || !googleEl) return;
    let cancelled = false;
    loadGoogle().then(() => {
      if (cancelled) return;
      const id = window.google!.accounts.id;
      if (googleInitializedFor !== clientId) {
        id.initialize({
          client_id: clientId,
          callback: (r) => finishRef.current(api('/auth/google', { body: { credential: r.credential } })),
        });
        googleInitializedFor = clientId;
      }
      id.renderButton(googleEl, { theme: 'outline', size: 'large', shape: 'pill', text: 'continue_with', width: 280 });
    }).catch(() => !cancelled && setError("Couldn't load Google sign-in. Check your connection and reload."));
    return () => { cancelled = true; };
  }, [clientId, googleEl]);

  return (
    <Center mih="100dvh" p="md">
      <Stack align="center" gap="lg" maw={340} w="100%">
        <Logo size={88} />
        <Stack gap={4} align="center">
          <Title order={1}>Porch Light</Title>
          <Text c="dimmed" ta="center">Trade favors, tools, and kid-watching with your dad friends.</Text>
        </Stack>

        {error && <Alert color="red" w="100%">{error}</Alert>}

        {clientId ? <div ref={setGoogleEl} style={{ minHeight: 44 }} /> : null}
        {config.data && !clientId && !config.data.devLogin && (
          <Alert color="gray" w="100%">Google sign-in isn't configured yet. See the README.</Alert>
        )}

        {config.data?.devLogin && <DevLogin busy={busy} onSubmit={(email, name) => finish(api('/auth/dev', { body: { email, name } }))} />}

        <Text size="xs" c="dimmed" ta="center">Invite-only. Ask a friend who runs a circle to invite your email.</Text>
      </Stack>
    </Center>
  );
}

function DevLogin({ busy, onSubmit }: { busy: boolean; onSubmit: (email: string, name: string) => void }) {
  const [email, setEmail] = useState('rob@example.com');
  const [name, setName] = useState('Rob');
  return (
    <Stack w="100%" gap="xs">
      <Divider label="Local dev sign-in" />
      <TextInput label="Name" value={name} onChange={(e) => setName(e.currentTarget.value)} />
      <TextInput label="Email" value={email} onChange={(e) => setEmail(e.currentTarget.value)} />
      <Button variant="default" loading={busy} onClick={() => onSubmit(email, name)}>Sign in (dev)</Button>
    </Stack>
  );
}
