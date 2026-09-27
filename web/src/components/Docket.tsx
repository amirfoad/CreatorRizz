import { useCallback, useEffect, useMemo, useState } from 'react';
import type { CreatorRizzClient } from '../api/client';
import type { ProductionList, ProductionListItem, ReviewKind } from '../api/contracts';
import { ApiError } from '../api/contracts';
import { GATES } from '../gates';
import { GateRail } from './GateRail';

const PAGE_SIZE = 25;

/**
 * The docket: everything on the desk, and the queue of what is waiting on a person. One list with a
 * filter, because "the backlog" and "the review queue" are the same rows read differently.
 */
export function Docket({ client, selectedId, onSelect }: {
  client: CreatorRizzClient;
  selectedId: string | null;
  onSelect: (item: ProductionListItem) => void;
}) {
  const [awaitingReview, setAwaitingReview] = useState<ReviewKind | ''>('');
  const [page, setPage] = useState<ProductionList | null>(null);
  const [skip, setSkip] = useState(0);
  const [failure, setFailure] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async (from: number, review: ReviewKind | '') => {
    setLoading(true);
    try {
      setPage(await client.listProductions({ awaitingReview: review || undefined, skip: from, take: PAGE_SIZE }));
      setFailure(null);
    } catch (error) {
      // The server's sentence, not ours. A reviewer who cannot get the docket cannot do the job.
      setFailure(error instanceof ApiError ? error.message : 'The docket could not be loaded.');
      setPage(null);
    } finally {
      setLoading(false);
    }
  }, [client]);

  useEffect(() => { void load(0, awaitingReview); }, [load, awaitingReview]);

  const waiting = useMemo(() => page?.items.filter((item) => item.awaitingReview !== null).length ?? 0, [page]);

  return (
    <section className="docket" aria-labelledby="docket-heading">
      <div className="docket-head">
        <h2 id="docket-heading">Docket</h2>
        <p className="docket-count">
          {waiting === 0 ? 'Nothing is waiting on a decision.' : `${waiting} waiting on a decision on this page.`}
        </p>
        <div className="docket-filters" role="group" aria-label="Filter the docket">
          <FilterButton label="Everything" active={awaitingReview === ''} onClick={() => { setAwaitingReview(''); setSkip(0); }} />
          {GATES.map((gate) => (
            <FilterButton
              key={gate}
              label={`${gate} review`}
              active={awaitingReview === gate}
              onClick={() => { setAwaitingReview(gate); setSkip(0); }}
            />
          ))}
        </div>
      </div>

      {failure && <p className="notice notice-failure" role="alert">{failure}</p>}
      {loading && !page && <p className="notice">Loading the docket…</p>}
      {page && page.items.length === 0 && !loading && <p className="notice">No production matches this filter.</p>}

      <ol className="docket-rows" aria-busy={loading}>
        {page?.items.map((item) => (
          <li key={item.id}>
            <button
              type="button"
              className={`docket-row${item.id === selectedId ? ' docket-row-selected' : ''}`}
              aria-current={item.id === selectedId}
              onClick={() => onSelect(item)}
            >
              <span className="docket-row-head">
                <GateRail state={item.state} awaitingReview={item.awaitingReview} />
                <span className="docket-row-state">{item.state}</span>
              </span>
              <span className="docket-row-meta">
                <span className="evidence-id">{item.id}</span>
                <span>version {item.version}</span>
                <span>{relative(item.createdAt)}</span>
              </span>
            </button>
          </li>
        ))}
      </ol>

      {page && (
        <nav className="pager" aria-label="Docket pages">
          <button type="button" disabled={skip === 0 || loading} onClick={() => { const from = Math.max(0, skip - PAGE_SIZE); setSkip(from); void load(from, awaitingReview); }}>
            Previous
          </button>
          <span className="pager-position">
            {page.totalCount === 0 ? 'none' : `${skip + 1}–${Math.min(skip + page.items.length, page.totalCount)} of ${page.totalCount}`}
          </span>
          <button type="button" disabled={!page.hasMore || loading} onClick={() => { const from = skip + PAGE_SIZE; setSkip(from); void load(from, awaitingReview); }}>
            Next
          </button>
        </nav>
      )}
    </section>
  );
}

function FilterButton({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return <button type="button" className={`filter${active ? ' filter-active' : ''}`} aria-pressed={active} onClick={onClick}>{label}</button>;
}

const UNITS: readonly [limit: number, unit: Intl.RelativeTimeFormatUnit][] = [
  [60, 'second'], [60, 'minute'], [24, 'hour'], [7, 'day'], [4.35, 'week'], [12, 'month'],
];

/** How long a production has been sitting there, which is what a queue is actually asking. */
export function relative(isoDate: string): string {
  const elapsed = Date.now() - new Date(isoDate).getTime();
  let value = elapsed / 1000;
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  for (const [limit, unit] of UNITS) {
    if (Math.abs(value) < limit || unit === 'month') return formatter.format(-Math.round(value / limit), unit);
    value /= limit;
  }
  return formatter.format(0, 'second');
}
