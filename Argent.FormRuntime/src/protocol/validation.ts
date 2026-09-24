import type { FormComponent, FormDefinition, FormField, FormValidator } from './definition.js';
import { evaluate, type FormValues, type JsonValue } from './expression.js';

export interface FormValueError {
  readonly field: string;
  readonly code: string;
  readonly message: string;
}

const decimalPattern = /^-?(0|[1-9]\d*)(\.\d+)?$/;
const datePattern = /^\d{4}-\d{2}-\d{2}$/;
const timestampPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/;
const emailPattern = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

export function validateForm(definition: FormDefinition, values: FormValues): readonly FormValueError[] {
  const active = activeObjectBindings(definition, values);
  return fields(definition.components)
    .filter(field => !field.objectBinding || active.has(field.objectBinding))
    .filter(field => !(definition.objects ?? []).some(binding =>
      active.has(binding.key) && binding.assignToBinding === field.objectBinding &&
      binding.assignToProperty === (field.propertyKey ?? field.name)))
    .flatMap(field => validateField(field, values));
}

export function activeObjectBindings(definition: FormDefinition, values: FormValues): Set<string> {
  const allFields = fields(definition.components);
  const bindings = definition.objects ?? [];
  const active = new Set(bindings.filter(binding => binding.isPrimary ||
    (binding.when ? evaluate(binding.when, values) : allFields.some(field =>
      field.objectBinding === binding.key && populated(field, values))))
    .map(binding => binding.key));
  let changed: boolean;
  do {
    changed = false;
    for (const binding of bindings) {
      if (active.has(binding.key) && binding.assignToBinding && !active.has(binding.assignToBinding)) {
        active.add(binding.assignToBinding);
        changed = true;
      }
    }
  } while (changed);
  return active;
}

function populated(field: FormField, values: FormValues): boolean {
  if (field.visibleWhen && !evaluate(field.visibleWhen, values)) return false;
  if (field.disabledWhen && evaluate(field.disabledWhen, values)) return false;
  const value = values[field.name];
  return value !== undefined && value !== null && value !== false &&
    (typeof value !== 'string' || value.trim() !== '') &&
    (!Array.isArray(value) || value.length > 0);
}

export function validateField(field: FormField, values: FormValues): readonly FormValueError[] {
  if (field.visibleWhen && !evaluate(field.visibleWhen, values)) return [];
  if (field.disabledWhen && evaluate(field.disabledWhen, values)) return [];

  const value = values[field.name];
  const required = field.required === true || (field.requiredWhen ? evaluate(field.requiredWhen, values) : false);
  if (required && isEmpty(value)) return [error(field, 'field.required', `${field.label} is required.`)];
  if (isEmpty(value)) return [];
  if (!hasExpectedType(field, value)) return [error(field, `type.${field.type}`, `${field.label} has an invalid value.`)];

  const errors: FormValueError[] = [];
  if (field.type === 'choice' && !field.reference && !field.options?.some(option => option.value === value && !option.disabled)) {
    errors.push(error(field, 'choice.invalid', `${field.label} contains an unavailable choice.`));
  }
  for (const validator of field.validators ?? []) {
    if (validator.when && !evaluate(validator.when, values)) continue;
    if (!passes(validator, value, values)) {
      errors.push(error(field, validator.code, validator.message ?? `${field.label} is invalid.`));
    }
  }
  return errors;
}

function hasExpectedType(field: FormField, value: JsonValue): boolean {
  switch (field.type) {
    case 'text': case 'choice': return typeof value === 'string';
    case 'integer': return typeof value === 'number' && Number.isSafeInteger(value);
    case 'decimal': return typeof value === 'string' && decimalPattern.test(value);
    case 'date': return typeof value === 'string' && validDate(value);
    case 'timestamp': return typeof value === 'string' && timestampPattern.test(value) && !Number.isNaN(Date.parse(value));
    case 'boolean': return typeof value === 'boolean';
    case 'file': return isAttachment(value);
    default: return false;
  }
}

function passes(validator: FormValidator, value: JsonValue, values: FormValues): boolean {
  switch (validator.type) {
    case 'required': return !isEmpty(value);
    case 'length': return typeof value === 'string' &&
      (validator.minLength === undefined || value.length >= validator.minLength) &&
      (validator.maxLength === undefined || value.length <= validator.maxLength);
    case 'range': {
      const number = decimalValue(value);
      const min = validator.min === undefined ? undefined : decimalValue(validator.min);
      const max = validator.max === undefined ? undefined : decimalValue(validator.max);
      return number !== undefined && (min === undefined || number >= min) && (max === undefined || number <= max);
    }
    case 'pattern':
      if (typeof value !== 'string' || !validator.pattern) return false;
      try { return new RegExp(validator.pattern).test(value); } catch { return false; }
    case 'email': return typeof value === 'string' && emailPattern.test(value);
    case 'url': return typeof value === 'string' && validHttpUrl(value);
    case 'compare': {
      if (!validator.otherField || !(validator.otherField in values)) return false;
      return evaluate({
        operator: validator.operator ?? 'equals',
        left: { value: value as never },
        right: { value: values[validator.otherField] as never }
      }, values);
    }
    default: return false;
  }
}

function validDate(value: string): boolean {
  if (!datePattern.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  const date = new Date(Date.UTC(year!, month! - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month! - 1 && date.getUTCDate() === day;
}

function validHttpUrl(value: string): boolean {
  try { return ['http:', 'https:'].includes(new URL(value).protocol); } catch { return false; }
}

function decimalValue(value: JsonValue): number | undefined {
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined;
  if (typeof value === 'string' && decimalPattern.test(value)) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : undefined;
  }
  return undefined;
}

function isAttachment(value: JsonValue): boolean {
  if (!value || Array.isArray(value) || typeof value !== 'object') return false;
  return typeof value.id === 'string' && typeof value.fileName === 'string' &&
    typeof value.contentType === 'string' && typeof value.size === 'number' &&
    Number.isSafeInteger(value.size) && value.size >= 0;
}

function isEmpty(value: JsonValue | undefined): boolean {
  return value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0);
}

function error(field: FormField, code: string, message: string): FormValueError {
  return { field: field.name, code, message };
}

function fields(components: readonly FormComponent[]): FormField[] {
  return components.flatMap(component => component.kind === 'field' ? [component] : fields(component.children));
}
