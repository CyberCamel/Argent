import { describe, expect, it } from 'vitest';
import { normalizeActions, resolveAction } from '../src/protocol/actions.js';

describe('task action presentation', () => {
  it('preserves legacy routing keys and gives only the final action primary styling', () => {
    expect(normalizeActions(['return', 'complete'])).toEqual([
      { key: 'return', label: 'return', appearance: 'secondary' },
      { key: 'complete', label: 'complete', appearance: 'primary' }
    ]);
  });

  it('resolves the routing key independently of the authored label and confirmation', () => {
    const actions = normalizeActions([
      { key: 'reject', label: 'Close application', appearance: 'danger', confirm: 'Close this application?' }
    ]);
    expect(resolveAction(actions, 'reject')?.key).toBe('reject');
    expect(resolveAction(actions, 'Close application')).toBeUndefined();
    expect(resolveAction(actions)?.confirm).toBe('Close this application?');
  });

  it('does not choose a route when an implicit submit has multiple actions', () => {
    expect(resolveAction(normalizeActions(['return', 'complete']))).toBeUndefined();
  });
});
