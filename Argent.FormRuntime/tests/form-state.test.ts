import { describe, expect, it } from 'vitest';
import type { FormDefinition } from '../src/protocol/definition.js';
import { FormState } from '../src/runtime/form-state.svelte.js';

const definition: FormDefinition = {
  protocolVersion: '2.0', id: 'citizen-request', objectKey: 'request',
  components: [
    { kind: 'field', type: 'boolean', name: 'contactMe', label: 'Contact me' },
    {
      kind: 'field', type: 'text', name: 'email', label: 'Email',
      visibleWhen: { operator: 'equals', left: { field: 'contactMe' }, right: { value: true } },
      requiredWhen: { operator: 'equals', left: { field: 'contactMe' }, right: { value: true } },
      validators: [{ type: 'email', code: 'email.invalid' }]
    }
  ]
};

describe('FormState', () => {
  it('only exposes live errors after a field is touched', () => {
    const state = new FormState(definition, { contactMe: true });
    const email = definition.components[1];
    if (email.kind !== 'field') throw new Error('Fixture is invalid.');

    expect(state.errors(email)).toEqual([]);
    state.touch('email');
    expect(state.errors(email).map(error => error.code)).toEqual(['field.required']);
  });

  it('re-evaluates conditional visibility and validation as values change', () => {
    const state = new FormState(definition);
    const email = definition.components[1];
    if (email.kind !== 'field') throw new Error('Fixture is invalid.');

    expect(state.visible(email)).toBe(false);
    expect(state.allErrors()).toEqual([]);
    state.setValue('contactMe', true);
    expect(state.visible(email)).toBe(true);
    expect(state.allErrors().map(error => error.code)).toEqual(['field.required']);
  });

  it('does not mutate the initial values object', () => {
    const initial = { contactMe: false };
    const state = new FormState(definition, initial);
    state.setValue('contactMe', true);
    expect(initial.contactMe).toBe(false);
  });
});
