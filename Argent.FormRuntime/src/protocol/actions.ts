export interface TaskActionDescriptor {
  readonly key: string;
  readonly label: string;
  readonly appearance: 'primary' | 'secondary' | 'danger';
  readonly confirm?: string | null;
}

// String actions remain accepted by embedded hosts using the original contract.
export function normalizeActions(actions: readonly (string | TaskActionDescriptor)[]): readonly TaskActionDescriptor[] {
  const explicitAppearance = actions.some(action => typeof action !== 'string');
  return actions.map((action, index) => typeof action === 'string'
    ? { key: action, label: action, appearance: !explicitAppearance && index === actions.length - 1 ? 'primary' : 'secondary' }
    : action);
}

export function resolveAction(actions: readonly TaskActionDescriptor[], key?: string): TaskActionDescriptor | undefined {
  return key ? actions.find(action => action.key === key) : actions.length === 1 ? actions[0] : undefined;
}
