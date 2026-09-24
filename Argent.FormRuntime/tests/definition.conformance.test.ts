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
  it('accepts a view mode context condition', () => {
    const definition: FormDefinition = {
      protocolVersion: '2.0', id: 'form-mode', objectKey: 'employee', viewModes: ['reviewer'],
      components: [{ kind: 'field', type: 'text', name: 'salary', label: 'Salary',
        visibleWhen: { operator: 'equals', left: { context: 'viewMode' }, right: { value: 'reviewer' } } }]
    };
    expect(compileDefinition(definition).isValid).toBe(true);
  });

  it('accepts a multi-mode membership condition', () => {
    const definition: FormDefinition = {
      protocolVersion: '2.0', id: 'form-modes', objectKey: 'employee', viewModes: ['HR', 'IT', 'Finance'],
      components: [{ kind: 'field', type: 'text', name: 'salary', label: 'Salary',
        visibleWhen: { operator: 'notIn', left: { context: 'viewMode' }, right: { value: ['HR', 'IT'] } } }]
    };
    expect(compileDefinition(definition).isValid).toBe(true);
  });

  it('accepts domain object keys with spaces when binding identifiers are valid', () => {
    const definition: FormDefinition = {
      protocolVersion: '2.0', id: 'form-test', objectKey: 'Viewmode POC',
      objects: [{ key: 'Viewmode_POC', objectKey: 'Viewmode POC', isPrimary: true }],
      components: [{ kind: 'field', type: 'decimal', name: 'Viewmode_POC.salary', label: 'Salary',
        objectBinding: 'Viewmode_POC', propertyKey: 'salary' }]
    };
    expect(compileDefinition(definition).isValid).toBe(true);
  });

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
