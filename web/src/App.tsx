import { useState } from 'react';
import { ScenariosPage } from './pages/ScenariosPage';
import { RulesPage } from './pages/RulesPage';
import { RunsPage } from './pages/RunsPage';

type Tab = 'runs' | 'scenarios' | 'rules';

export function App() {
  const [tab, setTab] = useState<Tab>('runs');

  return (
    <div className="app">
      <header className="app-header">
        <h1>API Regression Lab</h1>
        <span className="sub">同一批请求 · 基线 vs 候选 · 状态码 / 响应头 / JSON / 响应时间</span>
      </header>

      <nav className="tabs">
        <button className={tab === 'runs' ? 'active' : ''} onClick={() => setTab('runs')}>
          运行 Runs
        </button>
        <button className={tab === 'scenarios' ? 'active' : ''} onClick={() => setTab('scenarios')}>
          场景 Scenarios
        </button>
        <button className={tab === 'rules' ? 'active' : ''} onClick={() => setTab('rules')}>
          规则 Rules
        </button>
      </nav>

      {tab === 'runs' && <RunsPage />}
      {tab === 'scenarios' && <ScenariosPage />}
      {tab === 'rules' && <RulesPage />}
    </div>
  );
}
