import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { describe, expect, it, vi } from 'vitest';

const script = readFileSync(new URL('../../Argent.Web/wwwroot/js/task-actions.js', import.meta.url), 'utf8');

function host(confirmationAccepted = true) {
  let submit: (event: unknown) => void = () => {};
  const attributes = new Map<string, string>();
  const inputs: Array<{ name: string; value: string }> = [];
  const button = { name: 'action', value: 'reject', disabled: false, dataset: { confirm: 'Close application?' } };
  const confirm = vi.fn(() => confirmationAccepted);
  const form = {
    addEventListener: (_: string, listener: typeof submit) => { submit = listener; },
    getAttribute: (name: string) => attributes.get(name),
    setAttribute: (name: string, value: string) => attributes.set(name, value),
    append: (input: typeof inputs[number]) => inputs.push(input),
    querySelectorAll: () => [button]
  };
  runInNewContext(script, {
    document: { querySelectorAll: () => [form], createElement: () => ({}) },
    window: { confirm }
  });
  const event = { submitter: button, preventDefault: vi.fn() };
  return { submit: () => submit(event), button, event, inputs, attributes, confirm };
}

describe('task actions without a form definition', () => {
  it('preserves the routing key before disabling the native submitter and blocks repeat submits', () => {
    const page = host();
    page.submit();
    expect(page.inputs).toEqual([{ type: 'hidden', name: 'action', value: 'reject' }]);
    expect(page.button.disabled).toBe(true);
    expect(page.attributes.get('aria-busy')).toBe('true');
    page.submit();
    expect(page.event.preventDefault).toHaveBeenCalledOnce();
    expect(page.inputs).toHaveLength(1);
    expect(page.confirm).toHaveBeenCalledOnce();
  });

  it('leaves actions available when the user cancels the authored confirmation', () => {
    const page = host(false);
    page.submit();
    expect(page.event.preventDefault).toHaveBeenCalledOnce();
    expect(page.button.disabled).toBe(false);
    expect(page.inputs).toEqual([]);
    expect(page.attributes.has('aria-busy')).toBe(false);
  });
});
