import { useEffect, useState } from 'react';
import { api } from '../api';
import type { RuleSet } from '../types';
import { KeyValueEditor } from '../components/KeyValueEditor';

type FormState = {
  name: string;
  ignorePaths: string[];
  arraySortKeys: Record<string, string>;
  numericAbsTolerance: number;
  numericRelTolerance: number;
  dynamicPatterns: Record<string, string>;
  ignoreHeaders: string[];
};

const toForm = (r: RuleSet): FormState => ({
  name: r.name,
  ignorePaths: [...r.ignorePaths],
  arraySortKeys: { ...r.arraySortKeys },
  numericAbsTolerance: r.numericAbsTolerance,
  numericRelTolerance: r.numericRelTolerance,
  dynamicPatterns: { ...r.dynamicPatterns },
  ignoreHeaders: [...r.ignoreHeaders]
});

function StringListEditor({
  values,
  onChange,
  placeholder
}: {
  values: string[];
  onChange: (v: string[]) => void;
  placeholder: string;
}) {
  return (
    <div>
      {values.map((v, i) => (
        <div className="kvrow" key={i}>
          <input
            className="mono"
            value={v}
            onChange={(e) => onChange(values.map((x, j) => (j === i ? e.target.value : x)))}
            placeholder={placeholder}
          />
          <span />
          <button className="btn danger" onClick={() => onChange(values.filter((_, j) => j !== i))}>
            ×
          </button>
        </div>
      ))}
      <div className="kvrow">
        <button className="btn secondary" onClick={() => onChange([...values, ''])}>
          + 添加
        </button>
        <span />
        <span />
      </div>
    </div>
  );
}

export function RulesPage() {
  const [rules, setRules] = useState<RuleSet[]>([]);
  const [form, setForm] = useState<FormState | null>(null);
  const [editingSource, setEditingSource] = useState<RuleSet | null>(null);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState('');

  const reload = async () => {
    setRules(await api.rules());
  };
  useEffect(() => {
    reload().catch((e) => setError(String(e.message ?? e)));
  }, []);

  const groups = new Map<string, RuleSet[]>();
  for (const r of rules) {
    const list = groups.get(r.name) ?? [];
    list.push(r);
    groups.set(r.name, list);
  }

  const startNewVersion = (r: RuleSet) => {
    setEditingSource(r);
    setForm(toForm(r));
    setError('');
    setSaved('');
  };

  const startBlank = () => {
    setEditingSource(null);
    setForm({
      name: '',
      ignorePaths: ['$.meta.request_id'],
      arraySortKeys: {},
      numericAbsTolerance: 0.01,
      numericRelTolerance: 0,
      dynamicPatterns: {},
      ignoreHeaders: []
    });
    setError('');
    setSaved('');
  };

  const save = async () => {
    if (!form) return;
    setError('');
    setSaved('');
    try {
      const created = await api.createRule({
        ...form,
        ignorePaths: form.ignorePaths.filter(Boolean),
        ignoreHeaders: form.ignoreHeaders.filter(Boolean)
      });
      setSaved(`已保存为新版本 v${created.version}（旧版本与历史运行保持不变）`);
      setEditingSource(created);
      setForm(toForm(created));
      await reload();
    } catch (e) {
      setError(String((e as Error).message));
    }
  };

  return (
    <div className="panel">
      <div className="row">
        <h2 style={{ margin: 0 }}>比较规则（版本化）</h2>
        <span className="spacer" />
        {!form ? (
          <button className="btn" onClick={startBlank} data-testid="new-rule">
            + 新建规则集
          </button>
        ) : (
          <button className="btn secondary" onClick={() => setForm(null)}>
            收起
          </button>
        )}
      </div>

      {form && (
        <div className="diff-detail" data-testid="rule-form">
          <label className="field">
            <span className="lbl">
              规则集名称{editingSource && <>（再次保存将创建新版本，当前为 v{editingSource.version}）</>}
            </span>
            <input
              className="mono"
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              data-testid="rule-name"
            />
          </label>

          <h3>忽略 JSON 路径</h3>
          <StringListEditor
            values={form.ignorePaths}
            onChange={(v) => setForm({ ...form, ignorePaths: v })}
            placeholder="$.meta.request_id 或 $.items[*].ts"
          />

          <h3>数组按键排序（路径 → 键字段）</h3>
          <KeyValueEditor
            values={form.arraySortKeys}
            onChange={(v) => setForm({ ...form, arraySortKeys: v })}
            keyPlaceholder="$.items"
            valuePlaceholder="id"
          />

          <h3>浮点容差</h3>
          <div className="row">
            <label className="field" style={{ margin: 0 }}>
              <span className="lbl">绝对容差 |a−b| ≤</span>
              <input
                type="number"
                step="any"
                value={form.numericAbsTolerance}
                onChange={(e) => setForm({ ...form, numericAbsTolerance: Number(e.target.value) })}
              />
            </label>
            <label className="field" style={{ margin: 0 }}>
              <span className="lbl">相对容差 ≤</span>
              <input
                type="number"
                step="any"
                value={form.numericRelTolerance}
                onChange={(e) => setForm({ ...form, numericRelTolerance: Number(e.target.value) })}
              />
            </label>
          </div>

          <h3>动态值正则屏蔽（路径 → 正则，两边都匹配即视为相等）</h3>
          <KeyValueEditor
            values={form.dynamicPatterns}
            onChange={(v) => setForm({ ...form, dynamicPatterns: v })}
            keyPlaceholder="$.generated_at"
            valuePlaceholder={String.raw`\d{4}-\d{2}-\d{2}T.*Z`}
          />

          <h3>忽略响应头</h3>
          <StringListEditor
            values={form.ignoreHeaders}
            onChange={(v) => setForm({ ...form, ignoreHeaders: v })}
            placeholder="x-custom-header"
          />

          {error && <div className="error">{error}</div>}
          {saved && <div style={{ color: 'var(--ok)' }}>{saved}</div>}
          <div className="row" style={{ marginTop: 10 }}>
            <button className="btn" onClick={save} data-testid="save-rule">
              保存为新版本
            </button>
          </div>
        </div>
      )}

      <table>
        <thead>
          <tr>
            <th>名称</th>
            <th style={{ width: 80 }}>版本</th>
            <th style={{ width: 80 }}>状态</th>
            <th>规则摘要</th>
            <th style={{ width: 130 }} />
          </tr>
        </thead>
        <tbody>
          {[...groups.entries()].map(([name, versions]) =>
            [...versions]
              .sort((a, b) => b.version - a.version)
              .map((r, i) => (
                <tr key={r.id}>
                  <td>{name}</td>
                  <td className="mono">v{r.version}</td>
                  <td>{r.isActive ? <span className="badge Completed">active</span> : <span className="muted">历史</span>}</td>
                  <td className="mono muted" style={{ fontSize: 11.5 }}>
                    ignore {r.ignorePaths.length} · sort {Object.keys(r.arraySortKeys).length} ·
                    dyn {Object.keys(r.dynamicPatterns).length} · abs {r.numericAbsTolerance}
                  </td>
                  <td>
                    {i === 0 && (
                      <button className="btn secondary" onClick={() => startNewVersion(r)}>
                        出新版本
                      </button>
                    )}
                  </td>
                </tr>
              ))
          )}
        </tbody>
      </table>
    </div>
  );
}
