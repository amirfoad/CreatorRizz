import type { ProductionState, ReviewKind } from './api/contracts';

/**
 * The three gates a production has to pass, in order. This is the order the server's state machine
 * enforces; the rail has to draw it, so it is spelled out here. The rail is a reading of the state, not
 * a second source of authority — the audit trail in the evidence panel is the record.
 */
export const GATES: readonly ReviewKind[] = ['Script', 'Rights', 'Publish'];

export type GateStanding = 'passed' | 'waiting' | 'stopped' | 'locked';

export interface GateStandingSummary {
  gates: Record<ReviewKind, GateStanding>;
  /**
   * Set when the production is rejected or failed. The state does not record *which* gate turned it
   * down, only the review decision table knows, so the rail refuses to guess and says the run stopped
   * instead of marking a gate the reviewer never saw fail.
   */
  stopped: boolean;
}

/** States that mean a human has cleared this many gates. */
const GATES_CLEARED: Partial<Record<ProductionState, number>> = {
  ScriptApproved: 1, AssetsPreparing: 1, AssetsReady: 1,
  RightsReview: 1, RightsApproved: 2, Rendering: 2, Rendered: 2,
  PublishReview: 2, PublishApproved: 3, Uploading: 3, Published: 3,
};

const STATES_WITHOUT_A_GATE_HISTORY: readonly ProductionState[] = ['Rejected', 'Failed'];

/** Where this production stands at each gate, as far as the state alone can honestly say. */
export function gateStandings(state: ProductionState, awaitingReview: ReviewKind | null): GateStandingSummary {
  const stopped = STATES_WITHOUT_A_GATE_HISTORY.includes(state);
  const cleared = stopped ? undefined : GATES_CLEARED[state];
  const gates = Object.fromEntries(GATES.map((gate, index) => {
    const standing: GateStanding = awaitingReview === gate ? 'waiting'
      : stopped ? 'stopped'
      : cleared !== undefined && index < cleared ? 'passed'
      : 'locked';
    return [gate, standing];
  })) as Record<ReviewKind, GateStanding>;
  return { gates, stopped };
}
