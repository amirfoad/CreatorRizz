import type { ProductionState, ReviewKind } from '../api/contracts';
import { GATES, gateStandings } from '../gates';

/**
 * Three segments, one per gate, filled only where a human has passed it. The signature of this desk: a
 * reviewer reads a row's whole history and sees exactly which gate is in front of them without reading
 * a single word of state.
 */
export function GateRail({ state, awaitingReview }: { state: ProductionState; awaitingReview: ReviewKind | null }) {
  const { gates, stopped } = gateStandings(state, awaitingReview);

  return (
    <span className="gate-rail" role="img" aria-label={describe(gates, stopped)}>
      {GATES.map((gate) => (
        <span key={gate} className={`gate-segment gate-${gates[gate]}`} title={`${gate}: ${gates[gate]}`}>
          <span className="gate-segment-name">{gate}</span>
        </span>
      ))}
    </span>
  );
}

function describe(gates: Record<ReviewKind, string>, stopped: boolean): string {
  if (stopped) return 'Run stopped before a publish decision';
  const cleared = GATES.filter((gate) => gates[gate] === 'passed').length;
  const waiting = GATES.find((gate) => gates[gate] === 'waiting');
  return waiting ? `${cleared} gate${cleared === 1 ? '' : 's'} passed, waiting on ${waiting}` : `${cleared} of ${GATES.length} gates passed`;
}
