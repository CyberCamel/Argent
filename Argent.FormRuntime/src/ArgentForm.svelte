<svelte:options
  customElement={{
    tag: 'argent-form',
    shadow: 'none',
    props: {
      definition: { type: 'Object' },
      values: { type: 'Object' },
      actions: { type: 'Object' },
      messages: { type: 'Object' }
    }
  }}
/>

<script lang="ts">
  import { compileDefinition, type FormDefinition, type FormField, type FormRuntimeMessages } from './protocol/definition.js';
  import type { FormValues } from './protocol/expression.js';
  import { normalizeActions, resolveAction, type TaskActionDescriptor } from './protocol/actions.js';
  import { FormState } from './runtime/form-state.svelte.js';
  import RuntimeComponent from './runtime/RuntimeComponent.svelte';

  let { definition, values = {}, actions = [], messages = {} }: {
    definition?: FormDefinition; values?: FormValues; actions?: (string | TaskActionDescriptor)[]; messages?: FormRuntimeMessages
  } = $props();
  const element: HTMLElement = $host();
  let compilation = $derived(definition ? compileDefinition(definition) : undefined);
  let state = $derived(compilation?.definition ? new FormState(compilation.definition.definition, values) : undefined);
  let actionDescriptors = $derived(normalizeActions(actions));

  function submit(event: SubmitEvent): void {
    event.preventDefault();
    if (!state) return;
    const action = resolveAction(actionDescriptors, (event.submitter as HTMLButtonElement | null)?.value);
    if (actionDescriptors.length && !action) {
      element.querySelector<HTMLButtonElement>('button[type="submit"]')?.focus();
      return;
    }
    const errors = state.validateAll();
    if (errors.length) {
      queueMicrotask(() => element.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
      element.dispatchEvent(new CustomEvent('argent-invalid', { detail: { errors }, bubbles: true, composed: true }));
      return;
    }
    if (action?.confirm && !window.confirm(action.confirm)) return;
    element.dispatchEvent(new CustomEvent('argent-submit', {
      detail: {
        protocolVersion: '2.0', formId: definition!.id, values: { ...state.values },
        action: action?.key
      },
      bubbles: true,
      composed: true
    }));
  }

  function fileSelected(field: FormField, file: File): void {
    state?.touch(field.name);
    element.dispatchEvent(new CustomEvent('argent-file-selected', {
      detail: { field: field.name, file }, bubbles: true, composed: true
    }));
  }

  export function setServerErrors(errors: Array<{ field: string; code: string; message: string }>): void {
    state?.setServerErrors(errors);
    queueMicrotask(() => element.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
  }
</script>

{#if !definition}
  <div role="status">{messages.loading ?? 'Loading form…'}</div>
{:else if !compilation?.isValid}
  <div role="alert" data-argent-error="definition.invalid">
    {messages.invalidDefinition ?? 'This form cannot be displayed because its definition is invalid.'}
    <ul>{#each compilation?.errors ?? [] as error}<li>{error.path}: {error.message}</li>{/each}</ul>
  </div>
{:else if state}
  <form data-argent-form={definition.id} novalidate onsubmit={submit}>
    {#if definition.title}<h1>{definition.title}</h1>{/if}
    {#if state.showAllErrors && state.allErrors().length}
      <div class="argent-error-summary" aria-live="polite">{messages.errorSummary ?? 'Please correct the highlighted fields.'}</div>
    {/if}
    {#each definition.components as component}
      <RuntimeComponent {component} {state} {messages} onFileSelected={fileSelected} />
    {/each}
    {#if actions.length}
      <div class="argent-form-actions">
        {#each actionDescriptors as action}
          <button type="submit" name="action" value={action.key} data-appearance={action.appearance}>{action.label}</button>
        {/each}
      </div>
    {:else}
      <button type="submit">{messages.submit ?? 'Submit'}</button>
    {/if}
  </form>
{/if}

<style>
  form { display: grid; gap: 1rem; width: 100%; max-width: var(--argent-form-max-width, 100%); color: var(--argent-text, inherit); font-family: var(--argent-font-family, system-ui, sans-serif); }
  h1 { margin: 0 0 0.5rem; font-size: 1.75rem; line-height: 1.2; }
  button { justify-self: start; min-height: 2.75rem; padding: 0.625rem 1rem; border: 0; border-radius: 0.375rem; background: var(--argent-primary, #4f46e5); color: white; font: inherit; font-weight: 700; cursor: pointer; }
  button:focus-visible { outline: 3px solid var(--argent-focus, #4f46e5); outline-offset: 2px; }
  button[data-appearance='secondary'] { background: var(--argent-secondary, #e5e7eb); color: var(--argent-secondary-text, #111827); }
  button[data-appearance='danger'] { background: var(--argent-danger, #b91c1c); }
  button:disabled { opacity: 0.6; cursor: wait; }
  .argent-form-actions { display: flex; flex-wrap: wrap; gap: 0.5rem; }
  .argent-error-summary { color: var(--argent-error, #b91c1c); }
</style>
