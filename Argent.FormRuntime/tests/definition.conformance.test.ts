import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { compileDefinition, type FormDefinition } from '../src/protocol/definition.js';

interface DefinitionCase {
  readonly name: string;
  readonly definition: FormDefinition;
  readonly valid: boolean;
  readonly errorCodes: readonly string[];
  readonly dependents: Readonly<Record<string, readonly string[]>>;
}

interface DefinitionSuite {
  readonly protocolVersion: string;
  readonly cases: readonly DefinitionCase[];
}

const fixtureUrl = new URL('../../testdata/form-protocol/definition-conformance.json', import.meta.url);
const suite = JSON.parse(readFileSync(fixtureUrl, 'utf8')) as DefinitionSuite;

describe('form protocol v2 definition conformance', () => {
  it('uses the expected protocol version', () => expect(suite.protocolVersion).toBe('2.0'));

  for (const testCase of suite.cases) {
    it(testCase.name, () => {
      const result = compileDefinition(testCase.definition);
      expect(result.isValid).toBe(testCase.valid);
      expect(result.errors.map(error => error.code).sort()).toEqual([...testCase.errorCodes].sort());
      if (result.definition) {
        const actual = Object.fromEntries(
          Object.entries(result.definition.dependents).map(([field, targets]) => [field, [...targets].sort()])
        );
        expect(actual).toEqual(testCase.dependents);
      }
    });
  }
});
