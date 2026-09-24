import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import {
  dependencies,
  evaluate,
  FormProtocolError,
  type FormExpression,
  type FormValues
} from '../src/protocol/expression.js';

interface ConformanceCase {
  readonly name: string;
  readonly values: FormValues;
  readonly expression: FormExpression;
  readonly expected: boolean;
  readonly dependencies: readonly string[];
}

interface ConformanceSuite {
  readonly protocolVersion: string;
  readonly cases: readonly ConformanceCase[];
}

const fixtureUrl = new URL('../../testdata/form-protocol/condition-conformance.json', import.meta.url);
const suite = JSON.parse(readFileSync(fixtureUrl, 'utf8')) as ConformanceSuite;

describe('form protocol v2 expression conformance', () => {
  it('uses the expected protocol version', () => {
    expect(suite.protocolVersion).toBe('2.0');
  });

  for (const testCase of suite.cases) {
    it(testCase.name, () => {
      expect(evaluate(testCase.expression, testCase.values)).toBe(testCase.expected);
      expect([...dependencies(testCase.expression)].sort()).toEqual([...testCase.dependencies].sort());
    });
  }

  it('fails unknown operators explicitly', () => {
    expect(() => evaluate({ operator: 'executeDeveloperCode' }, {})).toThrow(FormProtocolError);
  });
});
