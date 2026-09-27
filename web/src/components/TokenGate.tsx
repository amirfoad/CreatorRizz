import { useState } from 'react';

/**
 * The API takes a bearer token on every endpoint but the health probes, and it never issues one. So the
 * first thing this desk does is ask for the reviewer's token. It is held in memory only: a reviewer
 * token that survives a page reload is a reviewer token sitting in local storage, and this is a gate
 * that can approve a publish.
 */
export function TokenGate({ onAccept }: { onAccept: (token: string) => void }) {
  const [token, setToken] = useState('');

  return (
    <section className="token-gate">
      <h2>Sign in to the desk</h2>
      <p>
        Every endpoint but <code>/health</code> needs a token, and the API validates tokens without issuing
        them. Create one with:
      </p>
      <pre className="evidence-id token-command">dotnet user-jwts create --project src/CreatorRizz.Api --role reviewer</pre>
      <p>Paste the token. It stays in this tab's memory and is gone on reload.</p>
      <form onSubmit={(event) => { event.preventDefault(); if (token.trim() !== '') onAccept(token.trim()); }}>
        <label className="token-field">
          <span className="visually-hidden">Reviewer token</span>
          <textarea
            value={token}
            onChange={(event) => setToken(event.target.value)}
            rows={4}
            spellCheck={false}
            placeholder="eyJhbGciOi…"
          />
        </label>
        <button type="submit" className="decision-approve" disabled={token.trim() === ''}>Open the docket</button>
      </form>
    </section>
  );
}
