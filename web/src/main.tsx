import { createRoot } from 'react-dom/client';
import './styles.css';

function App() {
  return <main className="app-shell">
    <header className="brand-bar">
      <img className="brand-logo" src="/branding/creatorrizz-logo.png" alt="CreatorRizz" />
      <div><h1>CreatorRizz</h1><p>Content operations</p></div>
    </header>
    <section className="workspace"><h2>Production workspace</h2><p>پایه داشبورد عملیاتی آماده است.</p></section>
  </main>;
}

createRoot(document.getElementById('root')!).render(<App />);
