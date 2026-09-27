/**
 * The operations desk's view of the API. Every type here mirrors a contract the server already has, so
 * a rename on either side is a compile error rather than a blank column in a table.
 */

export type ReviewKind = 'Script' | 'Rights' | 'Publish';
export type ReviewOutcome = 'Approve' | 'Rework' | 'Reject';

export type ProductionState =
  | 'Discovered' | 'Scored' | 'Researching' | 'ResearchReady' | 'ScriptDraft'
  | 'ScriptInReview' | 'ScriptApproved' | 'AssetsPreparing' | 'AssetsReady'
  | 'RightsReview' | 'RightsApproved' | 'Rendering' | 'Rendered'
  | 'PublishReview' | 'PublishApproved' | 'Uploading' | 'Published'
  | 'Rejected' | 'Failed';

export type RightsStatus = 'Licensed' | 'Owned' | 'Unknown' | 'Rejected';

export interface ProductionListItem {
  id: string;
  topicCandidateId: string;
  state: ProductionState;
  version: number;
  createdAt: string;
  awaitingReview: ReviewKind | null;
}

export interface ProductionList {
  items: ProductionListItem[];
  totalCount: number;
  skip: number;
  hasMore: boolean;
}

export interface ScriptVersion {
  id: string;
  productionId: string;
  version: number;
  body: string;
  claimMapJson: string;
  createdAt: string;
}

export interface Asset {
  id: string;
  objectKey: string;
  type: string;
  sourceUrl: string | null;
  rightsStatus: RightsStatus;
  licenseEvidence: string | null;
  checksum: string | null;
}

export interface AuditEvent {
  id: number;
  actor: string;
  action: string;
  entityType: string;
  entityId: string;
  payloadJson: string | null;
  occurredAt: string;
}

export interface ScriptGeneration {
  id: string;
  productionId: string;
  modelId: string;
  promptVersion: string;
  inputReferencesJson: string;
  body: string;
  claimMapJson: string;
  createdAt: string;
}

/** What the server refused, in its own words. A conflict carries the version that beat us. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly currentVersion?: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }

  get isVersionConflict(): boolean {
    return this.status === 409 && this.currentVersion !== undefined;
  }

  /**
   * The API answers a refusal with `{ error }`, `{ error, currentVersion }` or a ProblemDetails
   * document. Whichever it is, the server's own sentence is what a reviewer needs to read, so it is
   * surfaced rather than replaced with something vaguer.
   */
  static async read(response: Response): Promise<ApiError> {
    const body = await response.json().catch(() => null) as
      { error?: string; detail?: string; currentVersion?: number } | null;
    return new ApiError(
      response.status,
      body?.error ?? body?.detail ?? `${response.status} ${response.statusText}`,
      body?.currentVersion,
    );
  }
}
