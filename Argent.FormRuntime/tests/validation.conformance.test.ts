import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import type { FormDefinition } from '../src/protocol/definition.js';
import type { FormValues } from '../src/protocol/expression.js';
import { validateForm } from '../src/protocol/validation.js';

interface ExpectedError { readonly field: string; readonly code: string }
interface ValidationCase { readonly name: string; readonly values: FormValues; readonly errors: readonly ExpectedError[] }
interface ValidationSuite { readonly protocolVersion: string; readonly definition: FormDefinition; readonly cases: readonly ValidationCase[] }

const fixtureUrl = new URL('../../testdata/form-protocol/validation-conformance.json', import.meta.url);
const suite = JSON.parse(readFileSync(fixtureUrl, 'utf8')) as ValidationSuite;

describe('form protocol v2 validation conformance', () => {
  for (const testCase of suite.cases) {
    it(testCase.name, () => {
      const actual = validateForm(suite.definition, testCase.values)
        .map(({ field, code }) => ({ field, code }))
        .sort(byFieldAndCode);
      expect(actual).toEqual([...testCase.errors].sort(byFieldAndCode));
    });
  }
});

it('requires fields only while their object binding is active', () => {
  const definition: FormDefinition = {
    protocolVersion: '2.0', id: 'optionalCustomer', objectKey: 'invoice',
    objects: [
      { key: 'invoice', objectKey: 'invoice', isPrimary: true },
      { key: 'customer', objectKey: 'customer', when: {
        operator: 'equals', left: { field: 'createCustomer' }, right: { value: true }
      }, assignToBinding: 'invoice', assignToProperty: 'customer' }
    ],
    components: [
      { kind: 'field', type: 'boolean', name: 'createCustomer', label: 'Create customer' },
      { kind: 'field', type: 'choice', name: 'invoice.customer', label: 'Existing customer', objectBinding: 'invoice', propertyKey: 'customer', required: true,
        reference: { objectKey: 'customer', labelField: 'name' } },
      { kind: 'field', type: 'text', name: 'customer.name', label: 'Name', objectBinding: 'customer', required: true }
    ]
  };
  expect(validateForm(definition, { createCustomer: false, 'invoice.customer': 'existing-id' })).toEqual([]);
  expect(validateForm(definition, { createCustomer: true }))
    .toEqual([expect.objectContaining({ field: 'customer.name', code: 'field.required' })]);
});

it('allows an empty secondary object and requires its fields once populated', () => {
  const definition: FormDefinition = {
    protocolVersion: '2.0', id: 'progressiveInvoice', objectKey: 'invoice',
    objects: [
      { key: 'invoice', objectKey: 'invoice', isPrimary: true },
      { key: 'customer', objectKey: 'customer' }
    ],
    components: [
      { kind: 'field', type: 'text', name: 'invoice.number', label: 'Number', objectBinding: 'invoice', required: true },
      { kind: 'field', type: 'text', name: 'customer.name', label: 'Name', objectBinding: 'customer', required: true },
      { kind: 'field', type: 'text', name: 'customer.email', label: 'Email', objectBinding: 'customer' }
    ]
  };
  expect(validateForm(definition, { 'invoice.number': 'INV-1' })).toEqual([]);
  expect(validateForm(definition, { 'invoice.number': 'INV-1', 'customer.email': 'contact@example.com' }))
    .toEqual([expect.objectContaining({ field: 'customer.name', code: 'field.required' })]);
});

function byFieldAndCode(left: ExpectedError, right: ExpectedError): number {
  return left.field.localeCompare(right.field) || left.code.localeCompare(right.code);
}
