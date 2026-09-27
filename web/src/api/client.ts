import type {
  Asset,
  AuditEvent,
  ProductionList,
  ReviewKind,
  ReviewOutcome,
  ScriptGeneration,
  ScriptVersion,
} from './contracts';
import { ApiError } from './contracts';

/**
 * One place that knows how to talk to the API. Every call carries the reviewer's token; nothing here
 * reads a role or a name out of the URL or a form, because the server takes the reviewer identity from
 * the token and would ignore what we sent anyway.
 */
export class CreatorRizzClient {
  constructor(
    private readonly baseUrl: string,
    private readonly token: string,
  ) {}

  listProductions(filter: { state?: string; awaitingReview?: ReviewKind; skip?: number; take?: number }) {
    const query = new URLSearchParams();
    if (filter.state) query.set('state', filter.state);
    if (filter.awaitingReview) query.set('awaitingReview', filter.awaitingReview);
    if (filter.skip !== undefined) query.set('skip', String(filter.skip));
    if (filter.take !== undefined) query.set('take', String(filter.take));
    return this.send<ProductionList>(`/productions?${query.toString()}`);
  }

  async getScripts(productionId: string) {
    return this.send<ScriptVersion[]>(`/productions/${productionId}/scripts`);
  }

  getAssets(productionId: string) {
    return this.send<Asset[]>(`/productions/${productionId}/assets`);
  }

  getScriptGenerations(productionId: string) {
    return this.send<ScriptGeneration[]>(`/productions/${productionId}/script-generations`);
  }

  getAuditEvents(productionId: string) {
    return this.send<AuditEvent[]>(`/productions/${productionId}/audit-events`);
  }

  /**
   * The version the caller read has to travel with the decision, or two reviewers on the same gate
   * overwrite each other. It is read from the docket row, never derived from "the one I saw a moment
   * ago plus one".
   */
  decideReview(productionId: string, version: number, kind: ReviewKind, outcome: ReviewOutcome, notes: string | null) {
    return this.send<void>(`/productions/${productionId}/reviews/${kind}`, {
      method: 'POST',
      headers: { 'If-Match': `"${version}"`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ outcome, notes }),
    });
  }

  private async send<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, {
      ...init,
      headers: { Authorization: `Bearer ${this.token}`, Accept: 'application/json', ...init.headers },
    });

    if (!response.ok) throw await ApiError.read(response);
    if (response.status === 204) return undefined as T;
    return (await response.json()) as T;
  }
}
