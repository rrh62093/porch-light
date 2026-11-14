import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { notifications } from '@mantine/notifications';
import { api, ApiError, type MeResponse } from './api';

// ---------- Data hooks ----------

export function useApi<T>(key: unknown[], path: string | null) {
  return useQuery<T, ApiError>({
    queryKey: key,
    queryFn: () => api<T>(path!),
    enabled: path !== null,
  });
}

export function useMe() {
  return useQuery<MeResponse, ApiError>({ queryKey: ['me'], queryFn: () => api<MeResponse>('/me'), retry: false });
}

/**
 * A write to the API. On success everything is refetched (the pilot is small, and this keeps
 * counts and lists honest); on failure the API's friendly message is shown.
 */
export function useAction<TVars = void, TResult = unknown>(
  fn: (vars: TVars) => Promise<TResult>,
  opts: { success?: string; onSuccess?: (r: TResult, vars: TVars) => void } = {},
) {
  const qc = useQueryClient();
  return useMutation<TResult, ApiError, TVars>({
    mutationFn: fn,
    onSuccess: async (r, vars) => {
      await qc.invalidateQueries();
      if (opts.success) notifications.show({ message: opts.success, color: 'teal' });
      opts.onSuccess?.(r, vars);
    },
    onError: (e) => notifications.show({ message: e.message, color: 'red' }),
  });
}

// ---------- Formatting ----------

const dayFmt = new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
const timeFmt = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' });

export function when(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  return `${dayFmt.format(d)}, ${timeFmt.format(d)}`;
}

export function windowText(from: string | null, to: string | null): string | null {
  if (!from && !to) return null;
  if (from && to) {
    const a = new Date(from), b = new Date(to);
    return a.toDateString() === b.toDateString() ? `${when(from)} to ${timeFmt.format(b)}` : `${when(from)} to ${when(to)}`;
  }
  return from ? when(from) : `by ${when(to)}`;
}

export function dateOnly(iso: string | null | undefined): string {
  if (!iso) return '';
  const [y, m, d] = iso.split('-').map(Number);
  return dayFmt.format(new Date(y, m - 1, d));
}

/** Local calendar day of a timestamp. */
export function day(iso: string | null | undefined): string {
  return iso ? dayFmt.format(new Date(iso)) : '';
}

export function ago(iso: string): string {
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 60) return 'just now';
  if (s < 3600) return `${Math.floor(s / 60)}m ago`;
  if (s < 86400) return `${Math.floor(s / 3600)}h ago`;
  if (s < 86400 * 7) return `${Math.floor(s / 86400)}d ago`;
  return dayFmt.format(new Date(iso));
}

/** `<input type="datetime-local">` value <-> ISO string. */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
export function fromLocalInput(v: string): string | null {
  return v ? new Date(v).toISOString() : null;
}

export function todayIso(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export const legStatusLabel: Record<string, { label: string; color: string }> = {
  open: { label: 'Open', color: 'gray' },
  calledIn: { label: 'Called in', color: 'amber' },
  awaitingConfirmation: { label: 'Needs confirming', color: 'amber' },
  completed: { label: 'Done', color: 'teal' },
  closedOut: { label: 'Closed out', color: 'gray' },
};

export const requestStatusLabel: Record<string, { label: string; color: string }> = {
  open: { label: 'Open', color: 'amber' },
  inNegotiation: { label: 'Talking it over', color: 'blue' },
  accepted: { label: 'Deal made', color: 'teal' },
  closed: { label: 'Closed', color: 'gray' },
};
