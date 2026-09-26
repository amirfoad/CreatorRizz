import { createRoot } from 'react-dom/client';
import './styles.css';

function App() {
  return <main><h1>CreatorRizz</h1><p>پایه داشبورد عملیاتی آماده است.</p></main>;
}

createRoot(document.getElementById('root')!).render(<App />);
