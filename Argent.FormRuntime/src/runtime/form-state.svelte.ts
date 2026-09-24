import type { FormDefinition, FormField } from '../protocol/definition.js';
import { evaluate, type FormValues, type JsonValue } from '../protocol/expression.js';
import { validateForm, type FormValueError } from '../protocol/validation.js';

export class FormState {
  readonly definition: FormDefinition;
  values = $state<Record<string, JsonValue>>({});
  touched = $state<string[]>([]);
  showAllErrors = $state(false);
  serverErrors = $state<FormValueError[]>([]);

  constructor(definition: FormDefinition, initialValues: FormValues = {}) {
    this.definition = definition;
    this.values = { ...initialValues };
  }

  setValue(field: string, value: JsonValue | undefined): void {
    this.serverErrors = this.serverErrors.filter(error => error.field !== field);
    if (value === undefined) {
      const { [field]: _, ...remaining } = this.values;
      this.values = remaining;
    } else this.values = { ...this.values, [field]: value };
  }

  touch(field: string): void {
    if (!this.touched.includes(field)) this.touched = [...this.touched, field];
  }

  errors(field: FormField): readonly FormValueError[] {
    if (!this.showAllErrors && !this.touched.includes(field.name)) return [];
    return [...validateForm(this.definition, this.values).filter(error => error.field === field.name),
      ...this.serverErrors.filter(error => error.field === field.name)];
  }

  validateAll(): readonly FormValueError[] {
    this.showAllErrors = true;
    return this.allErrors();
  }

  allErrors(): readonly FormValueError[] { return [...validateForm(this.definition, this.values), ...this.serverErrors]; }

  setServerErrors(errors: readonly FormValueError[]): void {
    this.serverErrors = [...errors];
    this.showAllErrors = true;
  }

  visible(field: FormField): boolean {
    if (field.hidden) return false;
    const binding = this.definition.objects?.find(item => item.key === field.objectBinding);
    if (binding?.when && !evaluate(binding.when, this.values)) return false;
    if (this.definition.objects?.some(item => item.assignToBinding === field.objectBinding &&
      item.assignToProperty === (field.propertyKey ?? field.name) &&
      (!item.when || evaluate(item.when, this.values)))) return false;
    return !field.visibleWhen || evaluate(field.visibleWhen, this.values);
  }
  disabled(field: FormField): boolean { return !!field.disabledWhen && evaluate(field.disabledWhen, this.values); }
  readOnly(field: FormField): boolean { return !!field.readOnlyWhen && evaluate(field.readOnlyWhen, this.values); }
  required(field: FormField): boolean {
    return field.required === true || (!!field.requiredWhen && evaluate(field.requiredWhen, this.values));
  }
}
