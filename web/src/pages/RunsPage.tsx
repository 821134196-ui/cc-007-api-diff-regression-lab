import { useEffect, useRef, useState } from 'react';
import { api } from '../api';
import type { AppConfig, DiffEntry, RunResult, RunSummary } from '../types';

function pretty(body: string | null | undefined, isJson: boolean) {
  if (!body) return body ?? '';
  if (!isJson) return body;
  try {
    return JSON.stringify(JSON.parse(body), null, 2);
  } catch {
    return body;
  }
}

function DiffEntryLine({ d }: { d: DiffEntry }) {
  return (
    <div>
      <div className="diff-line">
        <span className="area">{d.area}</span>
        <span className="path mono">{d.path}</span>
      </div>
      <div className="diff-line exp">
        <span className="muted">基线</span>
        <span className="mono">{d.expected ?? '∅'}</span>
      </div>
      <div className="diff-line act">
        <span className="muted">候选</span>
        <span className="mono">{d.actual ?? '∅'}</span>
      </div>
    </div>
  );
}

function TargetPanel({ title, call }: { title: string; call: RunResult['baseline'] }) {
  return (
    <div>
      <h4>
        {title}{' '}
        {call.reached ? (
          <span className="badge Completed">{call.statusCode}</span>
        ) : (
          <span className="badge NetworkFailure">{call.errorKind}</span>
        )}{' '}
        <span className="muted">{call.elapsedMs} ms</span>
      </h4>
      {!call.reached && (
        <div className="error">
          网络失败（{call.errorKind}）：{call.errorDetail} —— 未收到任何 HTTP 响应，不会被当作空响应比较
        </div>
      )}
      <div className="mono muted" style={{ fontSize: 11.5, marginBottom: 4 }}>
        {Object.entries(call.headers)
          .map(([k, v]) => `${k}: ${v}`)
          .join('\n')}
      </div>
      <pre className="body">{pretty(call.bodyText, call.bodyIsJson)}</pre>
    </div>
  );
}

function ResultRow({ result }: { result: RunResult }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <tr className="result-row" onClick={() => setOpen((o) => !o)} data-testid={`result-${result.outcome}`}>
        <td className="mono">{result.orderIndex + 1}</td>
        <td>{result.scenarioName}</td>
        <td>
          <span className={`badge ${result.outcome}`}>{result.outcome}</span>
        </td>
        <td className="mono muted">{result.summary}</td>
        <td className="mono">
          {result.baseline.reached ? result.baseline.statusCode : `⚠${result.baseline.errorKind}`} /{' '}
          {result.candidate.reached ? result.candidate.statusCode : `⚠${result.candidate.errorKind}`}
        </td>
        <td className="mono muted">{result.diffs.length || ''}</td>
      </tr>
      {open && (
        <tr>
          <td colSpan={6}>
            <div className="diff-detail" data-testid="diff-detail">
              <div className="sidebyside">
                <TargetPanel title="基线" call={result.baseline} />
                <TargetPanel title="候选" call={result.candidate} />
              </div>
              {result.diffs.length > 0 && (
                <>
                  <h4>差异明细（{result.diffs.length}）</h4>
                  {result.diffs.map((d, i) => (
                    <DiffEntryLine key={i} d={d} />
                  ))}
                </>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

export function RunsPage() {
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [runs, setRuns] = useState<RunSummary[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<RunSummary | null>(null);
  const [results, setResults] = useState<RunResult[]>([]);
  const [error, setError] = useState('');

  const [baselineUrl, setBaselineUrl] = useState('');
  const [candidateUrl, setCandidateUrl] = useState('');
  const [concurrency, setConcurrency] = useState(4);
  const [timeoutMs, setTimeoutMs] = useState(10000);
  const [baselineRate, setBaselineRate] = useState(0);
  const [candidateRate, setCandidateRate] = useState(0);

  const pollRef = useRef<number | null>(null);

  useEffect(() => {
    api
      .config()
      .then((c) => {
        setConfig(c);
        setBaselineUrl(c.baselineBaseUrl);
        setCandidateUrl(c.candidateBaseUrl);
      })
      .catch((e) => setError(String(e.message ?? e)));
    api.runs().then((r) => {
      setRuns(r);
      if (r[0]) {
        setSelectedId(r[0].id);
      }
    });
  }, []);

  // Poll while the selected run is active.
  useEffect(() => {
    if (!selectedId) return;
    let stopped = false;

    const tick = async () => {
      const [run, rs] = await Promise.all([api.run(selectedId), api.results(selectedId)]);
      if (stopped) return;
      setSelected(run);
      setResults(rs);
      setRuns((prev) => {
        const others = prev.filter((r) => r.id !== run.id);
        return [run, ...others].sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1));
      });
      if (run.status === 'Running' || run.status === 'Pending') {
        pollRef.current = window.setTimeout(tick, 1000);
      } else {
        pollRef.current = null;
      }
    };
    tick().catch((e) => setError(String(e.message ?? e)));
    return () => {
      stopped = true;
      if (pollRef.current) window.clearTimeout(pollRef.current);
    };
  }, [selectedId]);

  const start = async () => {
    setError('');
    try {
      const run = await api.startRun({
        baselineBaseUrl: baselineUrl,
        candidateBaseUrl: candidateUrl,
        options: {
          concurrency,
          timeoutMs,
          baselineRateLimit: baselineRate,
          candidateRateLimit: candidateRate
        }
      });
      setSelectedId(run.id);
    } catch (e) {
      setError(String((e as Error).message));
    }
  };

  const cancel = async () => {
    if (!selectedId) return;
    await api.cancelRun(selectedId);
  };

  const active = selected?.status === 'Running' || selected?.status === 'Pending';
  const progress = selected && selected.totalScenarios > 0
    ? Math.round((selected.completedScenarios / selected.totalScenarios) * 100)
    : 0;

  return (
    <div>
      <div className="panel">
        <h2>启动一次运行</h2>
        <div className="kvgrid">
          <label className="field">
            <span className="lbl">基线服务 Base URL</span>
            <input className="mono" value={baselineUrl} onChange={(e) => setBaselineUrl(e.target.value)} />
          </label>
          <label className="field">
            <span className="lbl">候选服务 Base URL</span>
            <input className="mono" value={candidateUrl} onChange={(e) => setCandidateUrl(e.target.value)} />
          </label>
        </div>
        <div className="row">
          <label className="field" style={{ margin: 0 }}>
            <span className="lbl">任务并发数</span>
            <input type="number" min={1} max={64} value={concurrency}
              onChange={(e) => setConcurrency(Number(e.target.value))} />
          </label>
          <label className="field" style={{ margin: 0 }}>
            <span className="lbl">单请求超时 ms</span>
            <input type="number" min={100} value={timeoutMs}
              onChange={(e) => setTimeoutMs(Number(e.target.value))} />
          </label>
          <label className="field" style={{ margin: 0 }}>
            <span className="lbl">基线限速 rps（0=不限）</span>
            <input type="number" min={0} step="0.1" value={baselineRate}
              onChange={(e) => setBaselineRate(Number(e.target.value))} />
          </label>
          <label className="field" style={{ margin: 0 }}>
            <span className="lbl">候选限速 rps（0=不限）</span>
            <input type="number" min={0} step="0.1" value={candidateRate}
              onChange={(e) => setCandidateRate(Number(e.target.value))} />
          </label>
          <span className="spacer" />
          <button className="btn" onClick={start} data-testid="start-run">
            ▶ 启动运行
          </button>
          {active && (
            <button className="btn danger" onClick={cancel} data-testid="cancel-run">
              ■ 取消
            </button>
          )}
        </div>
        {error && <div className="error">{error}</div>}
      </div>

      {selected && (
        <div className="panel" data-testid="run-progress">
          <div className="row">
            <h2 style={{ margin: 0 }}>运行 {selected.id.slice(0, 8)}</h2>
            <span className={`badge ${selected.status}`}>{selected.status}</span>
            <span className="version-tag">
              规则 {selected.ruleSetName} v{selected.ruleSetVersion}（快照，历史版本不变）
            </span>
            <span className="spacer" />
            <span className="mono">
              {selected.completedScenarios}/{selected.totalScenarios} ·
              <span style={{ color: 'var(--bad)' }}> 差异 {selected.diffCount}</span> ·
              <span style={{ color: 'var(--net)' }}> 网络失败 {selected.networkFailureCount}</span>
            </span>
          </div>
          <div className="row" style={{ marginTop: 10 }}>
            <div className="progress" style={{ flex: 1 }}>
              <div style={{ width: `${progress}%` }} />
            </div>
            <span className="mono muted">{progress}%</span>
          </div>
        </div>
      )}

      <div className="panel">
        <h2>历史运行</h2>
        <table style={{ marginBottom: 16 }}>
          <thead>
            <tr>
              <th>ID</th>
              <th>状态</th>
              <th>规则版本</th>
              <th>进度</th>
              <th>差异/网络失败</th>
            </tr>
          </thead>
          <tbody>
            {runs.map((r) => (
              <tr
                key={r.id}
                className="result-row"
                style={{ fontWeight: r.id === selectedId ? 700 : 400 }}
                onClick={() => setSelectedId(r.id)}
              >
                <td className="mono">{r.id.slice(0, 8)}</td>
                <td>
                  <span className={`badge ${r.status}`}>{r.status}</span>
                </td>
                <td className="mono muted">
                  {r.ruleSetName} v{r.ruleSetVersion}
                </td>
                <td className="mono">
                  {r.completedScenarios}/{r.totalScenarios}
                </td>
                <td className="mono">
                  {r.diffCount} / {r.networkFailureCount}
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {selectedId && (
          <table>
            <thead>
              <tr>
                <th style={{ width: 40 }}>#</th>
                <th>场景</th>
                <th style={{ width: 140 }}>结果</th>
                <th>摘要</th>
                <th style={{ width: 160 }}>状态码 基/候</th>
                <th style={{ width: 60 }}>diff 数</th>
              </tr>
            </thead>
            <tbody>
              {results.map((r) => (
                <ResultRow key={r.id} result={r} />
              ))}
              {results.length === 0 && (
                <tr>
                  <td colSpan={6} className="muted">
                    暂无已完成场景…
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
