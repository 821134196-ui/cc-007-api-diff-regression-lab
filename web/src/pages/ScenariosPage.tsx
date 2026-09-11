import { useEffect, useRef, useState } from 'react';
import { api } from '../api';
import type { AppConfig, RuleSet, Scenario } from '../types';
import { KeyValueEditor, SecretChips, type InserterRef } from '../components/KeyValueEditor';

const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];

type FormState = {
  name: string;
  description: string;
  method: string;
  pathTemplate: string;
  pathParameters: Record<string, string>;
  queryParameters: Record<string, string>;
  headers: Record<string, string>;
  jsonBody: string;
  enabled: boolean;
};

const emptyForm = (): FormState => ({
  name: '',
  description: '',
  method: 'GET',
  pathTemplate: '/',
  pathParameters: {},
  queryParameters: {},
  headers: {},
  jsonBody: '',
  enabled: true
});

export function ScenariosPage() {
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [rules, setRules] = useState<RuleSet[]>([]);
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm());
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const inserterRef = useRef<InserterRef['current']>(null);

  const reload = async () => {
    const [sc, rs, cfg] = await Promise.all([api.scenarios(), api.rules(), api.config()]);
    setScenarios(sc);
    setRules(rs);
    setConfig(cfg);
    setLoading(false);
  };

  useEffect(() => {
    reload().catch((e) => setError(String(e.message ?? e)));
  }, []);

  const activeRule = rules.find((r) => r.isActive) ?? rules[0];

  const startCreate = () => {
    setEditingId('__new__');
    setForm(emptyForm());
    setError('');
  };

  const startEdit = (s: Scenario) => {
    setEditingId(s.id);
    setForm({
      name: s.name,
      description: s.description ?? '',
      method: s.method,
      pathTemplate: s.pathTemplate,
      pathParameters: { ...s.pathParameters },
      queryParameters: { ...s.queryParameters },
      headers: { ...s.headers },
      jsonBody: s.jsonBody ?? '',
      enabled: s.enabled
    });
    setError('');
  };

  const save = async () => {
    setError('');
    const payload = {
      ...form,
      ruleSetId: activeRule?.id,
      jsonBody: form.jsonBody.trim() ? form.jsonBody : null
    };
    try {
      if (editingId === '__new__') await api.createScenario(payload);
      else if (editingId) await api.updateScenario(editingId, payload);
      setEditingId(null);
      await reload();
    } catch (e) {
      setError(String((e as Error).message));
    }
  };

  const remove = async (id: string) => {
    if (!confirm('删除该场景？')) return;
    await api.deleteScenario(id);
    await reload();
  };

  if (loading) return <div className="muted">加载中…</div>;

  return (
    <div>
      <div className="panel">
        <div className="row">
          <h2 style={{ margin: 0 }}>请求场景</h2>
          <span className="spacer" />
          {editingId ? (
            <button className="btn secondary" onClick={() => setEditingId(null)}>
              取消编辑
            </button>
          ) : (
            <button className="btn" onClick={startCreate} data-testid="new-scenario">
              + 新建场景
            </button>
          )}
        </div>
        {activeRule && (
          <div className="hint">
            归属规则集：<b>{activeRule.name}</b> <span className="version-tag">v{activeRule.version}</span>
            （新建/编辑规则会产生新版本，历史运行保留当时版本）
          </div>
        )}

        {editingId && (
          <div className="diff-detail" data-testid="scenario-form">
            <label className="field">
              <span className="lbl">名称</span>
              <input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                data-testid="f-name"
              />
            </label>
            <label className="field">
              <span className="lbl">描述</span>
              <input
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
              />
            </label>
            <div className="kvgrid">
              <label className="field">
                <span className="lbl">方法</span>
                <select
                  value={form.method}
                  onChange={(e) => setForm({ ...form, method: e.target.value })}
                >
                  {METHODS.map((m) => (
                    <option key={m}>{m}</option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span className="lbl">路径（可带查询串，路径参数用 {'{name}'}）</span>
                <input
                  className="mono"
                  value={form.pathTemplate}
                  onChange={(e) => setForm({ ...form, pathTemplate: e.target.value })}
                  data-testid="f-path"
                />
              </label>
            </div>

            <h3>路径参数</h3>
            <KeyValueEditor
              values={form.pathParameters}
              onChange={(v) => setForm({ ...form, pathParameters: v })}
              keyPlaceholder="参数名"
              valuePlaceholder="值（可用 {{secret:NAME}}）"
              inserterRef={inserterRef}
            />

            <h3>查询参数</h3>
            <KeyValueEditor
              values={form.queryParameters}
              onChange={(v) => setForm({ ...form, queryParameters: v })}
              keyPlaceholder="参数名"
              valuePlaceholder="值（可用 {{secret:NAME}}）"
              inserterRef={inserterRef}
            />

            <h3>请求头</h3>
            <KeyValueEditor
              values={form.headers}
              onChange={(v) => setForm({ ...form, headers: v })}
              keyPlaceholder="Header 名"
              valuePlaceholder="值（可用 {{secret:NAME}}）"
              inserterRef={inserterRef}
            />

            {form.method !== 'GET' && (
              <>
                <h3>JSON Body</h3>
                <textarea
                  style={{ minHeight: 120 }}
                  value={form.jsonBody}
                  onChange={(e) => setForm({ ...form, jsonBody: e.target.value })}
                  onFocus={(e) => {
                    const el = e.target;
                    inserterRef.current = (token) => {
                      const start = el.selectionStart ?? el.value.length;
                      const end = el.selectionEnd ?? el.value.length;
                      setForm((f) => ({
                        ...f,
                        jsonBody: el.value.slice(0, start) + token + el.value.slice(end)
                      }));
                    };
                  }}
                  onBlur={() => (inserterRef.current = null)}
                  placeholder='{"key": "value"}'
                  data-testid="f-body"
                />
              </>
            )}

            {config && <SecretChips names={config.secretNames} inserterRef={inserterRef} />}

            <label className="field" style={{ marginTop: 10 }}>
              <span style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                <input
                  type="checkbox"
                  style={{ width: 'auto' }}
                  checked={form.enabled}
                  onChange={(e) => setForm({ ...form, enabled: e.target.checked })}
                />
                启用（运行时包含此场景）
              </span>
            </label>

            {error && <div className="error">{error}</div>}
            <div className="row">
              <button className="btn" onClick={save} data-testid="save-scenario">
                保存
              </button>
            </div>
          </div>
        )}

        <table>
          <thead>
            <tr>
              <th style={{ width: 70 }}>方法</th>
              <th>名称 / 路径</th>
              <th style={{ width: 200 }}>密钥引用</th>
              <th style={{ width: 70 }}>启用</th>
              <th style={{ width: 120 }} />
            </tr>
          </thead>
          <tbody>
            {scenarios.map((s) => (
              <tr key={s.id}>
                <td className="mono">{s.method}</td>
                <td>
                  <div>{s.name}</div>
                  <div className="mono muted">{s.pathTemplate}</div>
                </td>
                <td>
                  {s.secretRefs.map((r) => (
                    <span key={r} className="secret-chip">
                      {r}
                    </span>
                  ))}
                </td>
                <td>{s.enabled ? '✓' : '—'}</td>
                <td>
                  <button className="btn secondary" onClick={() => startEdit(s)}>
                    编辑
                  </button>{' '}
                  <button className="btn danger" onClick={() => remove(s.id)}>
                    删
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
