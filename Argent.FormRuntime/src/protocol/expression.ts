export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | JsonValue[] | { readonly [key: string]: JsonValue };
export type FormValues = Readonly<Record<string, JsonValue>>;

export interface FormOperand {
  readonly field?: string;
  readonly context?: 'viewMode';
  readonly value?: JsonPrimitive | JsonPrimitive[];
}

export interface FormExpression {
  readonly operator: string;
  readonly arguments?: readonly FormExpression[];
  readonly argument?: FormExpression;
  readonly left?: FormOperand;
  readonly right?: FormOperand;
  readonly operand?: FormOperand;
}

export class FormProtocolError extends Error {
  override readonly name = 'FormProtocolError';
}

export function evaluate(expression: FormExpression, values: FormValues): boolean {
  switch (expression.operator) {
    case 'and':
      return requiredArguments(expression).every(item => evaluate(item, values));
    case 'or':
      return requiredArguments(expression).some(item => evaluate(item, values));
    case 'not':
      if (!expression.argument) throw invalid(expression, 'argument');
      return !evaluate(expression.argument, values);
    case 'equals':
      return equal(expression, values);
    case 'notEquals':
      return !equal(expression, values);
    case 'in':
      return inList(expression, values);
    case 'notIn':
      return !inList(expression, values);
    case 'greaterThan':
      return order(expression, values) > 0;
    case 'greaterThanOrEqual':
      return order(expression, values) >= 0;
    case 'lessThan':
      return order(expression, values) < 0;
    case 'lessThanOrEqual':
      return order(expression, values) <= 0;
    case 'contains':
      return textOperation(expression, values, (left, right) => left.includes(right));
    case 'startsWith':
      return textOperation(expression, values, (left, right) => left.startsWith(right));
    case 'endsWith':
      return textOperation(expression, values, (left, right) => left.endsWith(right));
    case 'isEmpty':
      return isEmpty(resolve(requiredOperand(expression, 'operand'), values));
    case 'isNotEmpty':
      return !isEmpty(resolve(requiredOperand(expression, 'operand'), values));
    default:
      throw new FormProtocolError(`Unknown form expression operator '${expression.operator}'.`);
  }
}

export function dependencies(expression: FormExpression): ReadonlySet<string> {
  const fields = new Set<string>();
  collectDependencies(expression, fields);
  return fields;
}

function collectDependencies(expression: FormExpression, fields: Set<string>): void {
  addField(expression.left, fields);
  addField(expression.right, fields);
  addField(expression.operand, fields);
  if (expression.argument) collectDependencies(expression.argument, fields);
  expression.arguments?.forEach(argument => collectDependencies(argument, fields));
}

function addField(operand: FormOperand | undefined, fields: Set<string>): void {
  if (operand?.field) fields.add(operand.field);
}

function requiredArguments(expression: FormExpression): readonly FormExpression[] {
  if (!expression.arguments?.length) throw invalid(expression, 'non-empty arguments');
  return expression.arguments;
}

function requiredOperand(
  expression: FormExpression,
  member: 'left' | 'right' | 'operand'
): FormOperand {
  const operand = expression[member];
  if (!operand) throw invalid(expression, member);
  return operand;
}

function resolve(operand: FormOperand, values: FormValues): JsonValue | undefined {
  const hasField = typeof operand.field === 'string' && operand.field.length > 0;
  const hasValue = Object.hasOwn(operand, 'value');
  if (hasField === hasValue) {
    throw new FormProtocolError("A form operand must contain exactly one of 'field' or 'value'.");
  }
  if (hasField) {
    const value = values[operand.field!];
    return value;
  }
  return operand.value;
}

function equal(expression: FormExpression, values: FormValues): boolean {
  const left = resolve(requiredOperand(expression, 'left'), values);
  const right = resolve(requiredOperand(expression, 'right'), values);
  if (isNullish(left) || isNullish(right)) return isNullish(left) === isNullish(right);
  if (typeof left !== typeof right) {
    throw new FormProtocolError(
      `Operator '${expression.operator}' cannot compare ${kind(left)} with ${kind(right)}.`
    );
  }
  if (typeof left === 'string' || typeof left === 'number' || typeof left === 'boolean') {
    return left === right;
  }
  throw new FormProtocolError(`Operator '${expression.operator}' does not support ${kind(left)} operands.`);
}

function inList(expression: FormExpression, values: FormValues): boolean {
  const left = resolve(requiredOperand(expression, 'left'), values);
  const right = resolve(requiredOperand(expression, 'right'), values);
  if (!Array.isArray(right)) throw new FormProtocolError(`Operator '${expression.operator}' requires an array on the right.`);
  if (left === undefined) return false;
  return right.some(candidate => candidate === left);
}

function order(expression: FormExpression, values: FormValues): number {
  const left = resolve(requiredOperand(expression, 'left'), values);
  const right = resolve(requiredOperand(expression, 'right'), values);
  if (isNullish(left) || isNullish(right)) {
    throw new FormProtocolError(
      `Operator '${expression.operator}' does not order null or missing values.`
    );
  }
  if (typeof left !== typeof right) {
    throw new FormProtocolError(
      `Operator '${expression.operator}' cannot compare ${kind(left)} with ${kind(right)}.`
    );
  }
  if (typeof left === 'number' && typeof right === 'number') return Math.sign(left - right);
  if (typeof left === 'string' && typeof right === 'string') return ordinalCompare(left, right);
  throw new FormProtocolError(`Operator '${expression.operator}' does not order ${kind(left)} operands.`);
}

function ordinalCompare(left: string, right: string): number {
  if (left === right) return 0;
  return left < right ? -1 : 1;
}

function textOperation(
  expression: FormExpression,
  values: FormValues,
  operation: (left: string, right: string) => boolean
): boolean {
  const left = resolve(requiredOperand(expression, 'left'), values);
  const right = resolve(requiredOperand(expression, 'right'), values);
  if (typeof left !== 'string' || typeof right !== 'string') {
    throw new FormProtocolError(`Operator '${expression.operator}' requires string operands.`);
  }
  return operation(left, right);
}

function isEmpty(value: JsonValue | undefined): boolean {
  return value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0);
}

function isNullish(value: JsonValue | undefined): value is null | undefined {
  return value === undefined || value === null;
}

function kind(value: JsonValue): string {
  if (Array.isArray(value)) return 'Array';
  if (value === null) return 'Null';
  return typeof value;
}

function invalid(expression: FormExpression, member: string): FormProtocolError {
  return new FormProtocolError(`Operator '${expression.operator}' requires ${member}.`);
}
