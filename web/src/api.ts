// Thin fetch wrapper plus the shapes the API returns.

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function api<T = unknown>(path: string, opts: { method?: string; body?: unknown } = {}): Promise<T> {
  const res = await fetch(`/api${path}`, {
    method: opts.method ?? (opts.body !== undefined ? 'POST' : 'GET'),
    credentials: 'same-origin',
    headers: opts.body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
  });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  const data = text ? JSON.parse(text) : undefined;
  if (!res.ok) throw new ApiError(res.status, data?.error ?? `Request failed (${res.status})`);
  return data as T;
}

export interface Ref { id: string; name: string }

export interface NotificationPrefs { newRequests: boolean; responses: boolean; dealUpdates: boolean; reminders: boolean }
export interface Me { id: string; name: string; email: string; notifications: NotificationPrefs }
export interface MeResponse {
  user: Me;
  counts: { iOwe: number; owedToMe: number; needsConfirmation: number; pendingInvites: number; circles: number };
}
export interface AuthConfig { googleClientId: string | null; devLogin: boolean; vapidPublicKey: string | null }

export type Role = 'admin' | 'member';
export interface CircleSummary { id: string; name: string; role: Role; muted: boolean; memberCount: number; openRequests: number }
export interface CircleMemberRow {
  id: string; name: string; role: Role; joinedAt: string;
  offersChildcare: boolean; needsChildcare: boolean; skills: string[];
}
export interface CircleDetail {
  id: string; name: string; myRole: Role; muted: boolean;
  members: CircleMemberRow[];
  invites: { id: string; email: string; createdAt: string; invitedBy: string }[];
  openFlags: number;
}
export interface Invite { id: string; circle: Ref; invitedBy: string; createdAt: string }
export interface FlagRow {
  id: string; message: string; resolved: boolean; createdAt: string; reportedBy: Ref; about: Ref | null;
}

export type ItemKind = 'skill' | 'tool' | 'lookingFor';
export interface ProfileItem {
  id: string; kind: ItemKind; name: string; description: string | null; availableForTrade: boolean;
  endorsementCount: number; endorsedByMe: boolean; endorsers: string[];
}
export interface ChildProfile {
  offersChildcare: boolean; needsChildcare: boolean;
  kidsAges: string | null; allergies: string | null; contactNumber: string | null; notes: string | null;
}
export interface Person {
  id: string; name: string; email: string | null; isMe: boolean; items: ProfileItem[];
  offersChildcare: boolean; needsChildcare: boolean; childProfile: ChildProfile | null;
  sharedCircles: Ref[]; mutedByMe: boolean;
}

export type RequestStatus = 'open' | 'inNegotiation' | 'accepted' | 'closed';
export interface RequestSummary {
  id: string; title: string; description: string | null; status: RequestStatus;
  neededFrom: string | null; neededTo: string | null; suggestedRate: string | null;
  isChildcare: boolean; createdAt: string; isCalledIn: boolean;
  author: Ref; circle: Ref | null; recipient: Ref | null; offers: string[];
  responderCount: number; awaitingMe: boolean;
}
export type CounterKind = 'accept' | 'counter' | 'decline';
export type CounterStatus = 'pending' | 'accepted' | 'declined' | 'superseded';
export interface CounterMsg {
  id: string; responderId: string; kind: CounterKind; status: CounterStatus; message: string | null;
  customText: string | null; loose: boolean; customIsChildcare: boolean; proposedTime: string | null;
  createdAt: string; selectedOfferId: string | null; selectedOffer: string | null; author: Ref;
}
export interface Offer { id: string; description: string; isChildcare: boolean }
export interface RequestDetail extends Omit<RequestSummary, 'offers' | 'responderCount' | 'awaitingMe' | 'isCalledIn'> {
  childcareNote: string | null; isAuthor: boolean; canRespond: boolean;
  offers: Offer[];
  threads: { responder: Ref; messages: CounterMsg[] }[];
  dealId: string | null;
  calledIn: { id: string; description: string; kind: LegKind; status: LegStatus; dealId: string; scheduledAt: string | null } | null;
}

export type LegKind = 'specific' | 'loose';
export type LegStatus = 'open' | 'calledIn' | 'awaitingConfirmation' | 'completed' | 'closedOut';
export interface LedgerRow {
  id: string; dealId: string; description: string; kind: LegKind; status: LegStatus;
  scheduledAt: string | null; completedOn: string | null; calledInRequestId: string | null;
  iOwe: boolean; counterparty: Ref; dealTitle: string; createdAt: string;
}
export interface Ledger { iOwe: LedgerRow[]; owedToMe: LedgerRow[] }

export interface DealLeg {
  id: string; description: string; kind: LegKind; status: LegStatus; scheduledAt: string | null;
  completedOn: string | null; completedAt: string | null; calledInRequestId: string | null;
  owedBy: Ref; owedTo: Ref; iOwe: boolean;
}
export interface Deal {
  id: string; status: 'open' | 'completed' | 'cancelled'; createdAt: string; completedAt: string | null;
  involvesChildcare: boolean;
  request: { id: string; title: string; description: string | null; childcareNote: string | null };
  requester: Ref; responder: Ref;
  counterparty: Ref & { removedFromCircle: boolean };
  legs: DealLeg[];
  theirKids: ChildProfile | null;
  endorsable: { id: string; name: string; kind: ItemKind }[];
}
