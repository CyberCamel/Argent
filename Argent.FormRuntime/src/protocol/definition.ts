import { dependencies, FormProtocolError, type FormExpression } from './expression.js';

export interface FormDefinition {
  readonly protocolVersion: string;
  readonly id: string;
  readonly objectKey: string;
  readonly objects?: readonly FormObjectBinding[];
  readonly viewModes?: readonly string[];
  readonly title?: string;
  readonly components: readonly FormComponent[];
}

export interface FormObjectBinding {
  readonly key: string;
  readonly objectKey: string;
  readonly isPrimary?: boolean;
  readonly when?: FormExpression;
  readonly assignToBinding?: string;
  readonly assignToProperty?: string;
}

export interface FormRuntimeMessages {
  readonly loading?: string;
  readonly invalidDefinition?: string;
  readonly selectPlaceholder?: string;
  readonly errorSummary?: string;
  readonly submit?: string;
}

export type FormComponent = FormField | FormLayout;

export interface FormField {
  readonly kind: 'field';
  readonly type: string;
  readonly name: string;
  readonly objectBinding?: string;
  readonly propertyKey?: string;
  readonly label: string;
  readonly description?: string;
  readonly placeholder?: string;
  readonly required?: boolean;
  readonly hidden?: boolean;
  readonly modeOverrides?: Readonly<Record<string, { readonly hidden?: boolean; readonly required?: boolean | null }>>;
  readonly options?: readonly FormOption[];
  readonly reference?: FormReferenceSource;
  readonly validators?: readonly FormValidator[];
  readonly visibleWhen?: FormExpression;
  readonly requiredWhen?: FormExpression;
  readonly disabledWhen?: FormExpression;
  readonly readOnlyWhen?: FormExpression;
}

export interface FormReferenceSource {
  readonly objectKey: string;
  readonly labelField: string;
}

export interface FormOption {
  readonly value: string;
  readonly label: string;
  readonly disabled?: boolean;
}

export interface FormValidator {
  readonly type: string;
  readonly code: string;
  readonly message?: string;
  readonly when?: FormExpression;
  readonly minLength?: number;
  readonly maxLength?: number;
  readonly min?: string | number;
  readonly max?: string | number;
  readonly pattern?: string;
  readonly otherField?: string;
  readonly operator?: string;
}

export interface FormLayout {
  readonly kind: 'layout';
  readonly type: string;
  readonly id?: string;
  readonly title?: string;
  readonly visibleWhen?: FormExpression;
  readonly children: readonly FormComponent[];
}

export interface FormDefinitionError {
  readonly code: string;
  readonly path: string;
  readonly message: string;
}

export interface CompiledFormDefinition {
  readonly definition: FormDefinition;
  readonly dependents: Readonly<Record<string, ReadonlySet<string>>>;
}

export interface FormDefinitionCompilation {
  readonly definition?: CompiledFormDefinition;
  readonly errors: readonly FormDefinitionError[];
  readonly isValid: boolean;
}

const fieldTypes = new Set(['text', 'integer', 'decimal', 'date', 'timestamp', 'boolean', 'choice', 'file']);
const identifierPattern = /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/;
const validExternalKey = (key: string): boolean => typeof key === 'string' && key.trim().length > 0;
const layoutTypes = new Set(['section', 'row', 'column', 'tabs', 'accordion']);
const validatorTypes = new Set(['required', 'length', 'range', 'pattern', 'email', 'url', 'compare']);
const binaryOperators = new Set([
  'equals', 'notEquals', 'greaterThan', 'greaterThanOrEqual', 'lessThan', 'lessThanOrEqual',
  'contains', 'startsWith', 'endsWith'
]);

export function compileDefinition(definition: FormDefinition): FormDefinitionCompilation {
  const errors: FormDefinitionError[] = [];
  if (definition.protocolVersion !== '2.0') {
    errors.push({
      code: 'protocol.unsupported',
      path: 'protocolVersion',
      message: `Unsupported protocol version '${definition.protocolVersion}'.`
    });
  }

  const fields = new Map<string, string>();
  const viewModes = new Set(definition.viewModes ?? []);
  if (viewModes.size !== (definition.viewModes?.length ?? 0))
    errors.push({ code: 'form.view_mode_duplicate', path: 'viewModes', message: 'View mode keys must be unique.' });
  for (const mode of viewModes) if (!identifierPattern.test(mode))
    errors.push({ code: 'form.view_mode_invalid', path: 'viewModes', message: 'Invalid view mode key.' });
  const expressions: Array<{ path: string; expression: FormExpression }> = [];
  walk(definition.components, 'components', fields, expressions, errors);
  for (const field of allFields(definition.components))
    for (const mode of Object.keys(field.modeOverrides ?? {}))
      if (!viewModes.has(mode))
        errors.push({ code: 'field.view_mode_unknown', path: `components.${field.name}.modeOverrides`, message: `Unknown view mode '${mode}'.` });
  const objectKeys = new Set((definition.objects ?? []).map(binding => binding.key));
  if (!definition.objects?.length && !validExternalKey(definition.objectKey))
    errors.push({ code: 'form.object_key_invalid', path: 'objectKey', message: 'Domain object key is required.' });
  if (definition.objects?.length) {
    if (definition.objects.filter(binding => binding.isPrimary).length !== 1)
      errors.push({ code: 'form.primary_object_required', path: 'objects', message: 'Exactly one primary object binding is required.' });
    const seenKeys = new Set<string>();
    definition.objects.forEach((binding, index) => {
      if (!identifierPattern.test(binding.key))
        errors.push({ code: 'object.binding_invalid', path: `objects[${index}].key`, message: 'Invalid object binding.' });
      if (!validExternalKey(binding.objectKey))
        errors.push({ code: 'object.key_invalid', path: `objects[${index}].objectKey`, message: 'Domain object key is required.' });
      if (seenKeys.has(binding.key))
        errors.push({ code: 'object.binding_duplicate', path: `objects[${index}].key`, message: 'Object binding keys must be unique.' });
      seenKeys.add(binding.key);
      if (binding.isPrimary && binding.when)
        errors.push({ code: 'object.primary_conditional', path: `objects[${index}].when`, message: 'The primary object cannot be conditional.' });
      if (!!binding.assignToBinding !== !!binding.assignToProperty ||
          (binding.assignToBinding && !objectKeys.has(binding.assignToBinding)))
        errors.push({ code: 'object.assignment_invalid', path: `objects[${index}]`, message: 'Invalid object assignment.' });
      if (binding.when) {
        expressions.push({ path: `objects[${index}].when`, expression: binding.when });
        const ownFields = new Set([...allFields(definition.components)]
          .filter(field => field.objectBinding === binding.key).map(field => field.name));
        if ([...dependencies(binding.when)].some(field => ownFields.has(field)))
          errors.push({ code: 'object.condition_self_reference', path: `objects[${index}].when`,
            message: 'An object cannot be activated by one of its own fields.' });
      }
    });
  }
  const boundProperties = new Set<string>();
  for (const field of allFields(definition.components)) {
    if (field.objectBinding && !objectKeys.has(field.objectBinding))
      errors.push({ code: 'field.binding_unknown', path: `fields.${field.name}.objectBinding`, message: 'Unknown object binding.' });
    if (field.objectBinding) {
      if (field.propertyKey && !identifierPattern.test(field.propertyKey))
        errors.push({ code: 'field.property_invalid', path: `fields.${field.name}.propertyKey`, message: 'Invalid property key.' });
      const key = `${field.objectBinding}\0${field.propertyKey ?? field.name}`;
      if (boundProperties.has(key))
        errors.push({ code: 'field.property_duplicate', path: `fields.${field.name}.propertyKey`, message: 'A domain property can be bound only once.' });
      boundProperties.add(key);
    }
  }

  const mutableDependents = new Map<string, Set<string>>();
  for (const item of expressions) {
    try {
      validateExpressionShape(item.expression);
    } catch (error) {
      errors.push({
        code: 'expression.invalid',
        path: item.path,
        message: error instanceof Error ? error.message : 'Invalid expression.'
      });
      continue;
    }

    for (const dependency of dependencies(item.expression)) {
      if (!fields.has(dependency)) {
        errors.push({
          code: 'expression.field_unknown',
          path: item.path,
          message: `Expression references unknown field '${dependency}'.`
        });
        continue;
      }
      const targets = mutableDependents.get(dependency) ?? new Set<string>();
      targets.add(componentPath(item.path));
      mutableDependents.set(dependency, targets);
    }
  }

  if (errors.length > 0) return { errors, isValid: false };
  return {
    definition: {
      definition,
      dependents: Object.fromEntries(mutableDependents)
    },
    errors,
    isValid: true
  };
}

function* allFields(components: readonly FormComponent[]): Generator<FormField> {
  for (const component of components)
    if (component.kind === 'field') yield component;
    else yield* allFields(component.children);
}

function walk(
  components: readonly FormComponent[],
  path: string,
  fields: Map<string, string>,
  expressions: Array<{ path: string; expression: FormExpression }>,
  errors: FormDefinitionError[]
): void {
  components.forEach((component, index) => {
    const current = `${path}[${index}]`;
    if (component.kind === 'field') {
      if (!fieldTypes.has(component.type)) {
        errors.push({ code: 'field.type_unknown', path: `${current}.type`, message: `Unknown field type '${component.type}'.` });
      }
      if (fields.has(component.name)) {
        errors.push({ code: 'field.name_duplicate', path: `${current}.name`, message: `Field name '${component.name}' is duplicated.` });
      } else {
        fields.set(component.name, current);
      }
      addExpressions(component, current, expressions);
      validateFieldConfiguration(component, current, errors);
      return;
    }
    if (component.kind === 'layout') {
      if (!layoutTypes.has(component.type)) {
        errors.push({ code: 'layout.type_unknown', path: `${current}.type`, message: `Unknown layout type '${component.type}'.` });
      }
      if (component.visibleWhen) expressions.push({ path: `${current}.visibleWhen`, expression: component.visibleWhen });
      walk(component.children, `${current}.children`, fields, expressions, errors);
      return;
    }
    errors.push({ code: 'component.kind_unknown', path: current, message: 'Unknown component kind.' });
  });
}

function addExpressions(
  field: FormField,
  path: string,
  expressions: Array<{ path: string; expression: FormExpression }>
): void {
  const entries = [
    ['visibleWhen', field.visibleWhen],
    ['requiredWhen', field.requiredWhen],
    ['disabledWhen', field.disabledWhen],
    ['readOnlyWhen', field.readOnlyWhen]
  ] as const;
  for (const [name, expression] of entries) {
    if (expression) expressions.push({ path: `${path}.${name}`, expression });
  }
  field.validators?.forEach((validator, index) => {
    if (validator.when) expressions.push({ path: `${path}.validators[${index}].when`, expression: validator.when });
  });
}

function validateFieldConfiguration(field: FormField, path: string, errors: FormDefinitionError[]): void {
  if (field.reference) {
    if (field.type !== 'choice') {
      errors.push({ code: 'reference.field_type_invalid', path: `${path}.type`, message: "A reference source requires field type 'choice'." });
    }
    if (!validExternalKey(field.reference.objectKey)) {
      errors.push({ code: 'reference.object_key_invalid', path: `${path}.reference.objectKey`, message: 'Domain object key is required.' });
    }
    if (!identifierPattern.test(field.reference.labelField)) {
      errors.push({ code: 'reference.label_field_invalid', path: `${path}.reference.labelField`, message: `'${field.reference.labelField}' is not a valid protocol identifier.` });
    }
  }
  if (field.type === 'choice') {
    const values = new Set<string>();
    field.options?.forEach((option, index) => {
      if (values.has(option.value)) {
        errors.push({ code: 'choice.value_duplicate', path: `${path}.options[${index}].value`, message: `Choice value '${option.value}' is duplicated.` });
      }
      values.add(option.value);
    });
  }
  field.validators?.forEach((validator, index) => {
    const validatorPath = `${path}.validators[${index}]`;
    if (!validatorTypes.has(validator.type)) {
      errors.push({ code: 'validator.type_unknown', path: `${validatorPath}.type`, message: `Unknown validator type '${validator.type}'.` });
    }
    if (validator.type === 'compare' && !validator.otherField) {
      errors.push({ code: 'validator.configuration_invalid', path: validatorPath, message: 'Compare validator requires otherField.' });
    }
    if (validator.type === 'pattern' && !validator.pattern) {
      errors.push({ code: 'validator.configuration_invalid', path: validatorPath, message: 'Pattern validator requires pattern.' });
    }
  });
}

function validateExpressionShape(expression: FormExpression): void {
  if (expression.operator === 'and' || expression.operator === 'or') {
    if (!expression.arguments?.length) throw new FormProtocolError(`Operator '${expression.operator}' requires non-empty arguments.`);
    expression.arguments.forEach(validateExpressionShape);
    return;
  }
  if (expression.operator === 'not') {
    if (!expression.argument) throw new FormProtocolError("Operator 'not' requires argument.");
    validateExpressionShape(expression.argument);
    return;
  }
  if (binaryOperators.has(expression.operator)) {
    validateOperand(expression.left, expression.operator, 'left');
    validateOperand(expression.right, expression.operator, 'right');
    return;
  }
  if (expression.operator === 'in' || expression.operator === 'notIn') {
    validateOperand(expression.left, expression.operator, 'left');
    validateOperand(expression.right, expression.operator, 'right');
    const values = expression.right?.value;
    if (!Array.isArray(values) || values.length === 0 ||
        values.some(item => item !== null && !['string', 'number', 'boolean'].includes(typeof item)))
      throw new FormProtocolError(`Operator '${expression.operator}' requires a non-empty array of primitive values on the right.`);
    return;
  }
  if (expression.operator === 'isEmpty' || expression.operator === 'isNotEmpty') {
    validateOperand(expression.operand, expression.operator, 'operand');
    return;
  }
  throw new FormProtocolError(`Unknown form expression operator '${expression.operator}'.`);
}

function validateOperand(operand: { field?: string; context?: string; value?: unknown } | undefined, operator: string, member: string): void {
  if (!operand) throw new FormProtocolError(`Operator '${operator}' requires ${member}.`);
  const hasField = typeof operand.field === 'string' && operand.field.length > 0;
  const hasContext = operand.context === 'viewMode';
  const hasValue = Object.hasOwn(operand, 'value');
  if (Number(hasField) + Number(hasContext) + Number(hasValue) !== 1 ||
      (operand.context !== undefined && !hasContext)) {
    throw new FormProtocolError(`Operator '${operator}' ${member} must contain exactly one valid field, context or value.`);
  }
}

function componentPath(expressionPath: string): string {
  return expressionPath.slice(0, expressionPath.lastIndexOf('.'));
}
