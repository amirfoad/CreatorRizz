import { useMemo, useState } from 'react';
import { CreatorRizzClient } from './api/client';
import type { ProductionListItem } from './api/contracts';
import { Docket } from './components/Docket';
import { ReviewPanel } from './components/ReviewPanel';
import { TokenGate } from './components/TokenGate';

/** The API origin. Vite proxies /api to it so the browser never makes a cross-origin call. */
const API_BASE = '/api';

export function App() {
  const [token, setToken] = useState<string | null>(null);
  const [selected, setSelected] = useState<ProductionListItem | null>(null);
  // Bumping this makes both the docket and the panel read again, which is what a decision or a version
  // conflict needs: the version the reviewer was holding is no longer the version in the database.
  const [refresh, setRefresh] = useState(0);

  const client = useMemo(() => (token === null ? null : new CreatorRizzClient(API_BASE, token)), [token]);

  if (client === null) {
    return (
      <main className="app-shell">
        <BrandBar />
        <TokenGate onAccept={setToken} />
      </main>
    );
  }

  return (
    <main className="app-shell">
      <BrandBar>
        <button type="button" className="sign-out" onClick={() => { setToken(null); setSelected(null); }}>Sign out</button>
      </BrandBar>
      <div className="desk">
        <Docket key={refresh} client={client} selectedId={selected?.id ?? null} onSelect={setSelected} />
        {selected
          ? <ReviewPanel key={`${selected.id}-${refresh}`} client={client} item={selected} onDecided={() => setRefresh((value) => value + 1)} />
          : <section className="panel panel-empty">
            <h2>Evidence</h2>
            <p className="notice">Choose a production from the docket to read its script, its rights and its history.</p>
          </section>}
      </div>
    </main>
  );
}

function BrandBar({ children }: { children?: React.ReactNode }) {
  return (
    <header className="brand-bar">
      <img className="brand-logo" src="/branding/creatorrizz-logo.png" alt="CreatorRizz" />
      <div className="brand-text">
        <h1>CreatorRizz</h1>
        <p>Review desk</p>
      </div>
      {children}
    </header>
  );
}
