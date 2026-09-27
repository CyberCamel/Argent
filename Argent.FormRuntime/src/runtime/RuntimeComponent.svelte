<script lang="ts">
  import { evaluate } from '../protocol/expression.js';
  import type { FormComponent, FormField, FormRuntimeMessages } from '../protocol/definition.js';
  import type { FormState } from './form-state.svelte.js';
  import RuntimeComponent from './RuntimeComponent.svelte';

  let { component, state: formState, messages, onFileSelected }: {
    component: FormComponent;
    state: FormState;
    messages: FormRuntimeMessages;
    onFileSelected: (field: FormField, file: File) => void;
  } = $props();

  let field = $derived(component.kind === 'field' ? component : undefined);
  let layout = $derived(component.kind === 'layout' ? component : undefined);
  let errors = $derived(field ? formState.errors(field) : []);
  let visible = $derived(field ? formState.visible(field) : !!layout && (!layout.visibleWhen || evaluate(layout.visibleWhen, formState.values)));
  let activeTab = $state(0);
  const runtimeId = `argent-layout-${Math.random().toString(36).slice(2)}`;

  function childTitle(child: FormComponent, index: number): string {
    if (child.kind === 'layout') return child.title || `Section ${index + 1}`;
    return child.label || `Field ${index + 1}`;
  }

  function hasErrors(child: FormComponent): boolean {
    return child.kind === 'field'
      ? formState.errors(child).length > 0
      : child.children.some(hasErrors);
  }

  function handleTabKey(event: KeyboardEvent, index: number): void {
    if (!layout || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const last = layout.children.length - 1;
    activeTab = event.key === 'Home' ? 0 : event.key === 'End' ? last
      : event.key === 'ArrowRight' ? (index === last ? 0 : index + 1)
      : (index === 0 ? last : index - 1);
    queueMicrotask(() => document.getElementById(`${runtimeId}-tab-${activeTab}`)?.focus());
  }

  $effect(() => {
    if (layout?.type !== 'tabs' || !formState.showAllErrors) return;
    const invalidTab = layout.children.findIndex(hasErrors);
    if (invalidTab >= 0) activeTab = invalidTab;
  });

  function textValue(item: FormField): string {
    const value = formState.values[item.name];
    return typeof value === 'string' || typeof value === 'number' ? String(value) : '';
  }
  function setText(item: FormField, event: Event): void {
    const value = (event.currentTarget as HTMLInputElement).value;
    formState.setValue(item.name, value === '' ? undefined : value);
  }
  function setInteger(item: FormField, event: Event): void {
    const raw = (event.currentTarget as HTMLInputElement).value;
    if (raw === '') formState.setValue(item.name, undefined);
    else {
      const value = Number(raw);
      formState.setValue(item.name, Number.isSafeInteger(value) ? value : raw);
    }
  }
  function setBoolean(item: FormField, event: Event): void {
    formState.setValue(item.name, (event.currentTarget as HTMLInputElement).checked);
  }
  function selectFile(item: FormField, event: Event): void {
    const file = (event.currentTarget as HTMLInputElement).files?.[0];
    if (file) onFileSelected(item, file);
  }
  function openDatePicker(event: MouseEvent): void {
    const input = event.currentTarget as HTMLInputElement;
    if (input.disabled || input.readOnly) return;
    try {
      input.showPicker?.();
    } catch {
      input.focus();
    }
  }
  function errorId(item: FormField): string { return `argent-${item.name}-errors`; }
</script>

{#if visible}
  {#if layout}
    {#if layout.type === 'tabs'}
      <section class="argent-layout argent-tabs" data-layout={layout.type}>
        {#if layout.title}<h2>{layout.title}</h2>{/if}
        <div role="tablist" aria-label={layout.title ?? 'Form sections'}>
          {#each layout.children as child, index}
            <button type="button" role="tab" id={`${runtimeId}-tab-${index}`}
              aria-selected={activeTab === index} aria-controls={`${runtimeId}-panel-${index}`}
              tabindex={activeTab === index ? 0 : -1} onclick={() => activeTab = index}
              onkeydown={(event) => handleTabKey(event, index)}>
              {childTitle(child, index)}
            </button>
          {/each}
        </div>
        {#each layout.children as child, index}
          <div role="tabpanel" id={`${runtimeId}-panel-${index}`}
            aria-labelledby={`${runtimeId}-tab-${index}`} hidden={activeTab !== index}>
            <RuntimeComponent component={child} state={formState} {messages} {onFileSelected} />
          </div>
        {/each}
      </section>
    {:else if layout.type === 'accordion'}
      <section class="argent-layout argent-accordion" data-layout={layout.type}>
        {#if layout.title}<h2>{layout.title}</h2>{/if}
        {#each layout.children as child, index}
          <details open={index === 0}>
            <summary>{childTitle(child, index)}</summary>
            <RuntimeComponent component={child} state={formState} {messages} {onFileSelected} />
          </details>
        {/each}
      </section>
    {:else}
      <section class:argent-row={layout.type === 'row'} class="argent-layout" data-layout={layout.type}>
        {#if layout.title}<h2>{layout.title}</h2>{/if}
        {#each layout.children as child}
          <RuntimeComponent component={child} state={formState} {messages} {onFileSelected} />
        {/each}
      </section>
    {/if}
  {:else if field}
    <div class="argent-field" data-field={field.name} data-field-type={field.type}>
      {#if field.type === 'boolean'}
        <label class="argent-checkbox">
          <input type="checkbox" checked={formState.values[field.name] === true} disabled={formState.disabled(field)}
            onblur={() => formState.touch(field.name)} onchange={(event) => setBoolean(field, event)}
            aria-describedby={errors.length ? errorId(field) : undefined} />
          <span>{field.label}{#if formState.required(field)} <span aria-hidden="true">*</span>{/if}</span>
        </label>
      {:else}
        <label for={`argent-${field.name}`}>{field.label}{#if formState.required(field)} <span aria-hidden="true">*</span>{/if}</label>
        {#if field.type === 'choice'}
          <select id={`argent-${field.name}`} value={textValue(field)} disabled={formState.disabled(field)}
            onblur={() => formState.touch(field.name)} onchange={(event) => setText(field, event)}
            aria-invalid={errors.length > 0} aria-describedby={errors.length ? errorId(field) : undefined}>
            <option value="">{messages.selectPlaceholder ?? 'Select…'}</option>
            {#each field.options ?? [] as option}<option value={option.value} disabled={option.disabled}>{option.label}</option>{/each}
          </select>
        {:else if field.type === 'file'}
          <input id={`argent-${field.name}`} type="file" disabled={formState.disabled(field)}
            onblur={() => formState.touch(field.name)} onchange={(event) => selectFile(field, event)}
            aria-invalid={errors.length > 0} aria-describedby={errors.length ? errorId(field) : undefined} />
        {:else}
          <input id={`argent-${field.name}`}
            type={field.type === 'date' ? 'date' : field.type === 'integer' ? 'number' : 'text'}
            inputmode={field.type === 'decimal' ? 'decimal' : undefined} value={textValue(field)} placeholder={field.placeholder}
            disabled={formState.disabled(field)} readonly={formState.readOnly(field)}
            onclick={field.type === 'date' ? openDatePicker : undefined}
            oninput={(event) => field.type === 'integer' ? setInteger(field, event) : setText(field, event)}
            onblur={() => formState.touch(field.name)} aria-invalid={errors.length > 0}
            aria-describedby={errors.length ? errorId(field) : field.description ? `argent-${field.name}-description` : undefined} />
        {/if}
        {#if field.description}<p id={`argent-${field.name}-description`} class="argent-description">{field.description}</p>{/if}
      {/if}
      {#if errors.length}
        <ul id={errorId(field)} class="argent-errors" aria-live="polite">
          {#each errors as error}<li data-error-code={error.code}>{error.message}</li>{/each}
        </ul>
      {/if}
    </div>
  {/if}
{/if}

<style>
  .argent-layout {
    display: grid;
    gap: 1rem;
    margin: 0 0 1rem;
    padding: 0;
    border: 0;
  }

  .argent-row {
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
  }

  .argent-field {
    display: grid;
    gap: 0.375rem;
    min-width: 0;
  }

  label {
    font-weight: 500;
  }

  input:not([type='checkbox']),
  select {
    box-sizing: border-box;
    width: 100%;
    min-height: 2.75rem;           /* 44px touch target */
    padding: 0.5rem 0.625rem;
    border: 1px solid var(--argent-border, #c3cbd2);
    border-radius: var(--argent-radius, 0.3125rem);
    font: inherit;
    color: var(--argent-input-text, #2a343d);
    background: var(--argent-input-background, #fff);
    transition: border-color 0.12s ease, box-shadow 0.12s ease;
  }

  input:hover:not(:disabled):not([readonly]),
  select:hover:not(:disabled) {
    border-color: var(--argent-muted, #55626d);
  }

  input[type='date']:not(:disabled):not(:read-only) {
    cursor: pointer;
  }

  input:focus-visible,
  select:focus-visible {
    outline: none;
    border-color: var(--argent-focus, #4f46e5);
    box-shadow: 0 0 0 3px var(--argent-focus, #4f46e5);
  }

  [aria-invalid='true'] {
    border-color: var(--argent-error, #b3261e) !important;
  }

  .argent-checkbox {
    display: flex;
    align-items: center;
    gap: 0.625rem;
    min-height: 2.75rem;
  }

  .argent-checkbox input {
    width: 1rem;
    height: 1rem;
    accent-color: var(--argent-primary, #4f46e5);
  }

  .argent-description {
    margin: 0;
    color: var(--argent-muted, #55626d);
    font-size: 0.875rem;
  }

  .argent-errors {
    margin: 0;
    padding-left: 1.25rem;
    color: var(--argent-error, #b3261e);
  }

  [role='tablist'] {
    display: flex;
    gap: 0.25rem;
    border-bottom: 1px solid var(--argent-border, #c3cbd2);
  }

  [role='tab'] {
    min-height: 2.75rem;
    padding: 0.5rem 0.875rem;
    border: 0;
    border-bottom: 2px solid transparent;
    background: transparent;
    color: inherit;
    font: inherit;
    cursor: pointer;
  }

  [role='tab'][aria-selected='true'] {
    border-bottom-color: var(--argent-primary, #4f46e5);
    font-weight: 600;
  }

  [role='tab']:focus-visible,
  summary:focus-visible {
    outline: 2px solid var(--argent-focus, #4f46e5);
    outline-offset: 2px;
  }

  [role='tabpanel'] {
    padding-top: 1rem;
  }

  details {
    border: 1px solid var(--argent-border, #c3cbd2);
    border-radius: var(--argent-radius, 0.3125rem);
    padding: 0.75rem 1rem;
  }

  summary {
    cursor: pointer;
    font-weight: 600;
  }

  details > :global(.argent-layout),
  details > :global(.argent-field) {
    margin-top: 1rem;
  }
</style>
