import { useEffect, useState } from 'react';
import type { CreatorRizzClient } from '../api/client';
import type { Asset, AuditEvent, ProductionListItem, ReviewOutcome, ScriptGeneration, ScriptVersion } from '../api/contracts';
import { ApiError } from '../api/contracts';
import { GateRail } from './GateRail';

/**
 * The evidence, beside the buttons. A reviewer approves a script, a licence or a publish on the strength
 * of what is shown here, so a decision dialog that hides any of it would be asking for a judgement the
 * reviewer cannot make.
 */
export function ReviewPanel({ client, item, onDecided }: {
  client: CreatorRizzClient;
  item: ProductionListItem;
  onDecided: () => void;
}) {
  const [scripts, setScripts] = useState<ScriptVersion[]>([]);
  const [generations, setGenerations] = useState<ScriptGeneration[]>([]);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [audit, setAudit] = useState<AuditEvent[]>([]);
  const [notes, setNotes] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const [working, setWorking] = useState(false);

  useEffect(() => {
    // Switching rows resets the note. A note written for one script must not be sent with a decision
    // about another one.
    setNotes('');
    setFailure(null);
    setScripts([]); setGenerations([]); setAssets([]); setAudit([]);

    // One round trip each, in parallel. These are four small reads of the same production.
    void Promise.all([
      client.getScripts(item.id),
      client.getScriptGenerations(item.id),
      client.getAssets(item.id),
      client.getAuditEvents(item.id),
    ]).then(([loadedScripts, loadedGenerations, loadedAssets, loadedAudit]) => {
      setScripts(loadedScripts);
      setGenerations(loadedGenerations);
      setAssets(loadedAssets);
      setAudit(loadedAudit);
    }).catch((error: unknown) => {
      setFailure(error instanceof ApiError ? error.message : 'The evidence could not be loaded.');
    });
  }, [client, item.id]);

  async function decide(outcome: ReviewOutcome) {
    if (item.awaitingReview === null) return;
    setWorking(true);
    try {
      await client.decideReview(item.id, item.version, item.awaitingReview, outcome, notes.trim() === '' ? null : notes.trim());
      setFailure(null);
      onDecided();
    } catch (error) {
      if (error instanceof ApiError && error.isVersionConflict) {
        // Someone decided while this panel was open. Say so plainly instead of offering a retry that
        // would fail again for the same reason.
        setFailure(`Another reviewer decided first; this copy is at version ${error.currentVersion}. The docket has been reloaded.`);
        onDecided();
        return;
      }
      setFailure(error instanceof ApiError ? error.message : 'The decision could not be recorded.');
    } finally {
      setWorking(false);
    }
  }

  const script = scripts.at(-1);
  const generation = generations.at(-1);

  return (
    <section className="panel" aria-labelledby="panel-heading">
      <header className="panel-head">
        <div>
          <h2 id="panel-heading">Evidence</h2>
          <p className="panel-state">{item.state} · version {item.version}</p>
        </div>
        <GateRail state={item.state} awaitingReview={item.awaitingReview} />
      </header>

      {failure && <p className="notice notice-failure" role="alert">{failure}</p>}

      <div className="evidence">
        <div className="evidence-main">
          <h3>Narration</h3>
          {script
            ? <p className="narration">{script.body}</p>
            : <p className="notice">No script version has been written yet.</p>}

          {script && (
            <>
              <h3>Claim map</h3>
              <ClaimMap claimMapJson={script.claimMapJson} />
            </>
          )}

          {generation && (
            <>
              <h3>Provenance</h3>
              <p className="provenance">
                Written by <span className="evidence-id">{generation.modelId}</span> at prompt version{' '}
                <span className="evidence-id">{generation.promptVersion}</span> from {sourceCount(generation.inputReferencesJson)} sources.
              </p>
            </>
          )}
        </div>

        <aside className="evidence-side">
          <h3>Assets and rights</h3>
          {assets.length === 0
            ? <p className="notice">No asset is attached.</p>
            : <ul className="asset-list">
              {assets.map((asset) => (
                <li key={asset.id} className={`asset asset-${asset.rightsStatus.toLowerCase()}`}>
                  <span className="asset-type">{asset.type}</span>
                  <span className={`rights rights-${asset.rightsStatus.toLowerCase()}`}>{asset.rightsStatus}</span>
                  <span className="evidence-id">{asset.objectKey}</span>
                  {asset.licenseEvidence && <span className="asset-evidence">{asset.licenseEvidence}</span>}
                </li>
              ))}
            </ul>}
          {assets.some((asset) => asset.rightsStatus === 'Unknown' || asset.rightsStatus === 'Rejected') && (
            <p className="notice notice-failure">
              An asset with unresolved rights is attached. It can never reach render, whatever this gate decides.
            </p>
          )}

          <h3>Chain of custody</h3>
          {audit.length === 0
            ? <p className="notice">Nothing recorded yet.</p>
            : <ol className="audit-list">
              {[...audit].reverse().map((event) => (
                <li key={event.id}>
                  <span className="audit-action">{event.action}</span>
                  <span className="audit-actor">{event.actor}</span>
                  <span className="audit-when">{new Date(event.occurredAt).toLocaleString()}</span>
                  {event.payloadJson && <span className="audit-payload evidence-id">{event.payloadJson}</span>}
                </li>
              ))}
            </ol>}
        </aside>
      </div>

      <footer className="decision">
        {item.awaitingReview === null ? (
          <p className="notice">This production is not waiting on a decision.</p>
        ) : (
          <>
            <label className="decision-notes">
              <span>Note for the record <span className="decision-optional">optional</span></span>
              <textarea
                value={notes}
                onChange={(event) => setNotes(event.target.value)}
                rows={2}
                placeholder={`Why this ${item.awaitingReview.toLowerCase()} review is going this way`}
              />
            </label>
            <div className="decision-buttons">
              <button type="button" className="decision-approve" disabled={working} onClick={() => void decide('Approve')}>Approve</button>
              <button type="button" className="decision-rework" disabled={working} onClick={() => void decide('Rework')}>Send back</button>
              <button type="button" className="decision-reject" disabled={working} onClick={() => void decide('Reject')}>Reject</button>
            </div>
          </>
        )}
      </footer>
    </section>
  );
}

/** The claim map is JSON the API stores verbatim. It is shown formatted, never parsed into a guess. */
function ClaimMap({ claimMapJson }: { claimMapJson: string }) {
  try {
    const parsed: unknown = JSON.parse(claimMapJson);
    return <pre className="claim-map evidence-id">{JSON.stringify(parsed, null, 2)}</pre>;
  } catch {
    return <pre className="claim-map claim-map-broken evidence-id">{claimMapJson}</pre>;
  }
}

function sourceCount(inputReferencesJson: string): number {
  try {
    const parsed: unknown = JSON.parse(inputReferencesJson);
    return Array.isArray(parsed) ? parsed.length : 0;
  } catch {
    return 0;
  }
}
