import type { MutableRefObject } from 'react';
import { useState } from 'react';

export type SecretInserter = (token: string) => void;
export type InserterRef = MutableRefObject<SecretInserter | null>;

interface Props {
  values: Record<string, string>;
  onChange: (next: Record<string, string>) => void;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
  /** When provided, focusing a value input registers it for secret-chip insertion. */
  inserterRef?: InserterRef;
}

/** Editable string map with add/remove rows; value inputs register for secret insertion. */
export function KeyValueEditor({
  values,
  onChange,
  keyPlaceholder = 'name',
  valuePlaceholder = 'value',
  inserterRef
}: Props) {
  const [newKey, setNewKey] = useState('');
  const entries = Object.entries(values);

  const update = (oldKey: string, key: string, value: string) => {
    const next: Record<string, string> = {};
    for (const [k, v] of Object.entries(values)) {
      if (k === oldKey) next[key] = value;
      else next[k] = v;
    }
    onChange(next);
  };

  const remove = (key: string) => {
    const next = { ...values };
    delete next[key];
    onChange(next);
  };

  const add = () => {
    const k = newKey.trim();
    if (!k || k in values) return;
    onChange({ ...values, [k]: '' });
    setNewKey('');
  };

  return (
    <div>
      {entries.map(([k, v]) => (
        <div className="kvrow" key={k}>
          <input value={k} onChange={(e) => update(k, e.target.value, v)} placeholder={keyPlaceholder} />
          <input
            value={v}
            onChange={(e) => update(k, k, e.target.value)}
            onFocus={(e) => {
              if (!inserterRef) return;
              const el = e.target;
              inserterRef.current = (token) => {
                const start = el.selectionStart ?? el.value.length;
                const end = el.selectionEnd ?? el.value.length;
                update(k, k, el.value.slice(0, start) + token + el.value.slice(end));
              };
            }}
            onBlur={() => {
              if (inserterRef) inserterRef.current = null;
            }}
            placeholder={valuePlaceholder}
          />
          <button type="button" className="btn danger" onClick={() => remove(k)}>
            ×
          </button>
        </div>
      ))}
      <div className="kvrow">
        <input
          value={newKey}
          onChange={(e) => setNewKey(e.target.value)}
          placeholder={keyPlaceholder}
          onKeyDown={(e) => e.key === 'Enter' && add()}
        />
        <button type="button" className="btn secondary" onClick={add}>
          + 添加
        </button>
        <span />
      </div>
    </div>
  );
}

export function SecretChips({ names, inserterRef }: { names: string[]; inserterRef: InserterRef }) {
  if (names.length === 0) return null;
  return (
    <div className="hint">
      密钥只能引用服务端环境变量（先聚焦目标输入框，再点击引用插入，页面永不回显明文）：
      <div>
        {names.map((n) => (
          <span
            key={n}
            className="secret-chip"
            title={`插入 {{secret:${n}}}`}
            onMouseDown={(e) => e.preventDefault() /* 保持目标输入框焦点 */}
            onClick={() => inserterRef.current?.(`{{secret:${n}}}`)}
          >
            {'{{secret:' + n + '}}'}
          </span>
        ))}
      </div>
    </div>
  );
}
