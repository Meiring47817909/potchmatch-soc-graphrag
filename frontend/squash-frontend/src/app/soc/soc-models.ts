// TypeScript shapes mirroring the backend JSON (ASP.NET Core serialises records as camelCase).

export type AlertStatus = 'OPEN' | 'ACKNOWLEDGED' | 'RESOLVED';

// SocService AlertResponse.cs
export interface SocAlert {
  alertId: string;
  timestamp: string;
  ruleName: string;
  severity: string;
  status: string;
  description: string;
  relatedEvents: string[];
  affectedService: string | null;
  affectedUser: string | null;
  affectedEntity: string | null;
  recommendedAction: string;
}

// SocService SocEventResponse.cs — the 13 spec event fields.
export interface SocEvent {
  eventId: string;
  timestamp: string;
  serviceName: string;
  eventType: string;
  severity: string;
  userId: string | null;
  sourceIp: string | null;
  endpoint: string | null;
  httpMethod: string | null;
  statusCode: number | null;
  message: string;
  correlationId: string | null;
  affectedEntity: string | null;
}

export interface EventFilter {
  type?: string;
  user?: string;
  excludeType?: string;
  alertId?: string;
  page?: number;
  pageSize?: number;
}

export interface AlertFilter {
  status?: string;
  severity?: string;
  ruleName?: string;
}

// GraphService GraphQueryResult (GET /api/graph/incidents)
export interface GraphQueryResult {
  query: string;
  cypher: string;
  parameters: Record<string, unknown>;
  nodes: EvidenceNode[];
  relationships: EvidenceRel[];
  rows: Record<string, unknown>[];
}

// One row of GraphQueries.Incidents.
export interface GraphIncident {
  incidentId: string;
  title: string;
  subject: string;
  severity: string;
  status: string;
  escalated: boolean;
  priority: string | null;
  openedAt: string | null;
  lastAlertAt: string | null;
  /** Distinct ruleName of every alert the incident CONTAINS, sorted. */
  rules?: string[];
}

// RagService
export interface RagStatus {
  enabled: boolean;
  model: string | null;
  reason: string | null;
}

export interface AskRequest {
  question: string;
  alertId?: string;
  incidentId?: string;
}

export interface EvidenceNode {
  id: string;
  label: string;
  properties: Record<string, unknown>;
}

export interface EvidenceRel {
  id: string;
  type: string;
  start: string;
  end: string;
}

export interface SourceRef {
  id: string;
  name: string | null;
  url: string | null;
}

export interface RetrievalInfo {
  method: string;
  model: string;
  attempts: number;
  explanation: string | null;
  rowCount: number;
  truncated: boolean;
  unverifiedCitations: string[];
}

export interface RagAnswer {
  question: string;
  answered: boolean;
  answer: string;
  mode: string;
  retrieval: RetrievalInfo;
  cypher: string | null;
  parameters: Record<string, unknown>;
  nodes: EvidenceNode[];
  relationships: EvidenceRel[];
  rows: Record<string, unknown>[];
  sources: SourceRef[];
}

// Emitted by the panels, consumed by SocPage -> Ask panel.
export interface AskAbout {
  question: string;
  alertId?: string;
  incidentId?: string;
}

export interface AskContext {
  alertId?: string;
  incidentId?: string;
}
