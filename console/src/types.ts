export type Plan = {
  id: string;
  name: string;
  price: number;
  seatLimit: number;
};

export type Organization = {
  id: string;
  name: string;
  host: string;
  idpIssuer: string;
  directoryId: string;
};

export type Subscription = {
  id: string;
  organizationId: string;
  planId: string;
  startsOn: string;
  endsOn: string;
};

export type Team = {
  id: string;
  organizationId: string;
  name: string;
  goal: string;
};

export type TeamMember = {
  id: string;
  teamId: string;
  name: string;
  contact: string;
  paper: string;
  responsibilities: string;
  state: string;
};

export type AccessLog = {
  occurredAt: string;
  correlation: string;
  subject: string;
  organizationId: string;
  method: string;
  path: string;
  status: number;
  latencyMs: number;
};

export type Member = {
  id: string;
  organizationId: string;
  email: string;
  displayName: string;
  role: string;
};

export type MemberSave = {
  member: Member;
  inviteCode: string;
};

export type MachineKey = {
  id: string;
  name: string;
  prefix: string;
  lastUsedOn: string | null;
  revokedOn: string | null;
};

export type IssuedKey = {
  secret: string;
};

export type UsageSnapshot = {
  calls: number;
  callLimit: number;
  seats: number;
  seatLimit: number;
  cost: number;
  status: string;
};

export type EngineGrant = {
  id: string;
  engine: string;
  sessionCeiling: number;
  enabled: boolean;
};

export type AuditEntry = {
  occurredAt: string;
  actor: string;
  action: string;
  resource: string;
  outcome: string;
};

export type CallRecord = {
  correlation: string;
  engine: string;
  durationMs: number;
  cause: string;
  recordingConsented: boolean;
};

export type HealthRow = {
  name: string;
  status: string;
  detail: string;
};

export type PolicyRule = {
  id: string;
  resource: string;
  role: string;
  action: string;
  effect: string;
};

export type AccountStatus = {
  setupRequired: boolean;
  provider: string;
};

export type PasskeyOptions = {
  challengeId: string;
  challenge: string;
  rpId: string;
  userId: string;
  userName: string;
  credentialIds?: string[];
};

export type AccountResult = {
  status?: string;
  token?: string;
  challenge?: string;
  hint?: string;
  detail?: string;
  options?: PasskeyOptions;
};

export type Confirmation = {
  id: string;
  title: string;
  warning: string;
  detail: string;
  confirm: string;
};

export type Cell = string | number | boolean | null;

export type AccountProfile = {
  subject: string;
  organization: string;
  roles: string[];
  email: string;
  displayName: string;
};

export type ScreenProps = {
  token: string;
  role: string;
  focus: string;
};
