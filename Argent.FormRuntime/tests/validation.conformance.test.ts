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

function byFieldAndCode(left: ExpectedError, right: ExpectedError): number {
  return left.field.localeCompare(right.field) || left.code.localeCompare(right.code);
}
