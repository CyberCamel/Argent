# Argent architecture

Argent is a .NET 10 application for defining domain data, designing forms and workflows, and running workflows against records. SQL Server is the shared store. The web app owns the UI and HTTP APIs; a separate worker executes workflow work. `Argent.Host` can start both alongside a SQL Server container for local development.

## Projects and boundaries

| Project | Responsibility |
| --- | --- |
| `Argent.Core` | Shared entities, workflow and form definitions, protocol types, and service/store interfaces. It has no dependency on the application projects. |
| `Argent.Infrastructure` | `ArgentDbContext`, SQL Server EF Core mappings and migrations, and persistence-specific entities. |
| `Argent.Runtime` | Implementations of the core interfaces: workflow execution, EF-backed designer stores, form submission, domain records, authorization, and data source providers. |
| `Argent.Web` | ASP.NET Core entry point, Razor Pages, HTTP APIs, authentication, startup migrations, and development seeding. |
| `Argent.WebComponents` | Shared Blazor workflow and form designers and other interactive UI components. |
| `Argent.Web.Client` | WebAssembly host for shared components; registers HTTP-backed stores that call the web APIs. |
| `Argent.FormRuntime` | Svelte/TypeScript `<argent-form>` custom element used to render published forms in the browser. |
| `Argent.Engine` | Background host for the workflow execution service. |
| `Argent.Host` | Aspire app host that starts SQL Server, the web app, and then the engine. |
| `Argent.Worker.Clients` | Client libraries that run Argent worker tasks in other languages. The Python client is implemented; PowerShell and Node are planned. Not a .NET project, so it is not in `Argent.slnx`. |

`Argent.Runtime.Tests` and `Argent.WebComponents.Tests` cover runtime and UI logic. The TypeScript form runtime has its own tests and build in `Argent.FormRuntime`.

## Startup and persistence

[`Argent.Web/Program.cs`](Argent.Web/Program.cs) registers Razor Pages, interactive server and WebAssembly components, Identity, the designer and runtime APIs, and the service implementations. It applies EF migrations before serving requests. Development startup also runs [`DbInitializer`](Argent.Web/DbInitializer.cs) to seed users and sample domain data. [`Argent.Engine/Program.cs`](Argent.Engine/Program.cs) registers the same persistence and runtime services plus the hosted workflow engine. The Aspire host waits for the web app so migrations run before the engine starts.

[`ArgentDbContext`](Argent.Infrastructure/Data/ArgentDbContext.cs) stores Identity users and groups, policy documents, data sources, domain object definitions and records, form drafts/versions/submissions, workflow drafts/versions/instances, tokens, work items, user tasks, timers, workers and worker requests, audit entries, branding, and Data Protection keys. Migrations live in `Argent.Infrastructure/Migrations`. Both processes connect to the `ArgentDB` SQL Server database through [`AddArgentPersistence`](Argent.Runtime/DependencyInjection/PersistenceExtensions.cs).

## Main data flows

### Designing and publishing

The Blazor workflow designer (`WorkflowModeler` and `DesignerService` in `Argent.WebComponents/Workflows/Modeler`) edits a `WorkflowDefinition`. It loads and saves through `IWorkflowDesignerStore`: the server uses [`EfWorkflowDesignerStore`](Argent.Runtime/Workflows/Stores/EfWorkflowDesignerStore.cs), while WebAssembly uses an HTTP-backed implementation. Drafts are mutable; publishing creates a version, and deploying makes a version eligible for new instances. The same pattern is used for form definitions by `FormDesignerService` and [`EfFormDesignerStore`](Argent.Runtime/Forms/Stores/EfFormDesignerStore.cs), and for domain object definitions by [`DomainObjectDefinitionService`](Argent.Runtime/DomainObjects/DomainObjectDefinitionService.cs). [`DesignerApi`](Argent.Web/Api/DesignerApi.cs) exposes these operations to the browser.

## Workflow modeler

The modeler is split so that interaction rules, routing and the component markup stay separate. `WorkflowModeler.razor` renders; `WorkflowModeler.razor.cs` holds the pointer and keyboard handling; the rules live in services that are testable without a browser.

| Piece | Responsibility |
| --- | --- |
| `Interaction/ModelerInteractionController` | One gesture = begin, update, commit, cancel. Captures geometry, routing and selection once at the start; one completed gesture becomes one undo entry; cancelling restores the capture. Autosave is suspended while a gesture is open. |
| `Interaction/DesignerSnapshot` | The complete restorable canvas state: object membership, geometry, per-connection routing and route intent, and selection. |
| `Interaction/GeometryEditor` | Every geometry gesture in one place: node, group, pool, resize and space-tool operations, all measured from the gesture baseline so repeated pointer positions cannot drift. Boundary events follow their parent exactly once. |
| `Undo/DesignerHistory` | The modeler-wide undo/redo stack. Geometry and structural changes restore a snapshot; property edits are recorded per property and consecutive edits of one field collapse into a single step. |
| `Routing/ElasticRouter` | Rebuilds a connection from the user's stored route intent so a manually bent path stretches instead of being replaced. Falls back to a valid automatic route when the intent cannot be honoured, keeping the intent so it returns when the geometry allows. Rendering only reads `DesignerConnection.RouteSuspended`; it never recomputes geometry. |
| `Routing/RoutingService` | The automatic orthogonal router and the shared port/anchor helpers. |
| `Editing/*` | Structural and clipboard operations: splitting a connection, reconnecting an endpoint, copy/paste/duplicate with identity remapping, alignment and distribution. |
| `Navigation/CanvasNavigation` | Zoom to cursor, fit to content and focus, shared with the read-only instance overview so both behave the same. |

### Placing a node from the toolbox

Pressing a toolbox item arms a *pending node*: a ghost preview follows the pointer and is
placed on release over the canvas. Placement runs through the single
`PlaceNodeFromToolbox` entry point, which is also what a second click on the canvas uses,
so both gestures get the same boundary snapping, pool clamping and connection splitting.

Two details are load-bearing and easy to regress:

- **Placement must be handled on pointer *up* as well as pointer *down*.** A drag that
  starts on the toolbox never delivers a `pointerdown` to the canvas, so a down-only
  implementation cannot place anything: it arms a node that only a second click can
  commit.
- **`pointerleave` must not place.** The canvas binds `pointerleave` to the same
  finishing logic as `pointerup`, so a drag carried out of the canvas — and released over
  the sidebar or the properties panel — would otherwise drop a node where the user did
  not intend. `OnPointerLeave` finishes any real gesture but deliberately leaves a pending
  node armed, so the user can drag back in.

`pointercancel` and `blur` do the opposite and clear the pending node: those mean the
browser or the window took the gesture away, so nothing half-armed should survive.

### Route intent

Connections carry a stable `Id` and an optional `ConnectionRoute` in
[`Argent.Core/Workflows/Modeler/ConnectionRoute.cs`](Argent.Core/Workflows/Modeler/ConnectionRoute.cs). The route records the port sides the user preferred, the direction structure of the path they edited, and the axis lines they positioned — a vertical line by its `x`, a horizontal line by its `y`. Calculated waypoints are never persisted.

Constraints are matched to lines by ordinal *and* only applied while the stored structure still matches, so inserting or removing a bend can never silently apply a constraint to an unrelated segment. When the intent cannot produce a valid route the canvas shows a valid automatic route instead and the intent is retained; the user's route returns as soon as the geometry allows it again. Connections and route intent travel with the definition, so drafts, published versions, the instance overview, duplication and copy/paste all reproduce the same layout. Definitions written before route metadata existed load as automatic with no migration.

### Performance

Native routing measurements live in `Argent.WebComponents.Tests`. Browser behaviour — pointer-to-render latency, frame intervals and release-time work — is gated separately by the Playwright suite in [`tests/modeler-perf`](tests/modeler-perf/README.md), which records the reference hardware with every result. A .NET microbenchmark is not evidence of frame pacing in WebAssembly, and an event-rate cap is not an acceptable substitute for the gate.


### Records and forms

A published domain object definition describes record fields and validation. [`DomainObjectStore`](Argent.Runtime/DomainObjects/DomainObjectStore.cs) reads and writes records in the managed JSON record store, and can query configured external data sources. [`DataSourceRunner`](Argent.Runtime/DataSources/DataSourceRunner.cs) dispatches SQL, REST, or SOAP requests to the matching provider.

For a live form, the web page loads `<argent-form>` and browser code fetches a bootstrap payload from [`RuntimeDataApi`](Argent.Web/Api/RuntimeDataApi.cs). [`FormRuntimeService`](Argent.Runtime/Forms/FormRuntimeService.cs) selects a published form version, validates it, and supplies initial values. A form can contain named object bindings; fields map to properties on each binding, and a binding can create a record or update one attached to the workflow. Binding keys stay stable across the start form and later task forms so an update can resolve the same record. Conditional bindings support cases such as creating a new customer instead of choosing one from a dropdown. The Svelte element handles rendering and client-side interaction; the server validates again, writes the affected records in a transaction, and records a submission receipt to recognize retries. The shared Form Protocol v2 schema is in [`schemas/argent-form-v2.schema.json`](schemas/argent-form-v2.schema.json).

### Workflow execution and tasks

Starting a workflow through [`WorkflowInstanceService`](Argent.Runtime/Workflows/Execution/WorkflowInstanceService.cs) selects the latest deployed version and creates an instance, token, and pending work item in one transaction. The instance keeps a primary record ID and a map of named object bindings to record IDs, so later task forms can update the correct records. Each instance remains pinned to its workflow version. [`WorkflowEngine`](Argent.Runtime/Workflows/Execution/WorkflowEngine.cs) claims pending work, handles timers and recovery, and passes claimed items to [`TokenRunner`](Argent.Runtime/Workflows/Execution/TokenRunner.cs). The runner resolves a node handler (activities, gateways, events, or user tasks), updates tokens and work items, and records audit events. User activities create `UserTask` rows; [`TaskInboxService`](Argent.Runtime/Workflows/Execution/TaskInboxService.cs) supports listing, claiming, releasing, and completing them. The web task pages and runtime task API call those services, and completed tasks allow execution to continue.

Workflow start forms and task forms reuse `FormRuntimeService`. A start submission creates the populated records, while a task submission updates bindings already attached to the instance and creates newly populated bindings. A form can define named view modes with per-field visibility and requiredness; each user activity selects a mode from its form, and the runtime applies that mode both when rendering and when validating the task. Form-created records may be incomplete until later tasks enrich them. The instance overview and detail pages read execution state through `IWorkflowInstanceViewStore`.

### External workers

A `WorkerActivity` node delegates a unit of work to a process outside the engine. The node names a worker and a subject, and supplies key/value parameters whose values may contain the same `{{variable}}` placeholders REST activity templates interpolate. The engine never learns what the work is: the worker owns its own task configuration, and its outputs come back as key/value pairs that become the token's process variables.

Waiting works the same way user tasks do. [`WorkerActivityHandler`](Argent.Runtime/Workflows/Handlers/WorkerActivityHandler.cs) enqueues a `WorkerRequest` through [`IWorkerRequestQueue`](Argent.Core/Workers/IWorkerRequestQueue.cs) and returns `Waiting`, parking the work item. Completing a request releases that work item back to `Pending` in the same transaction, so the engine re-claims the node and the handler turns the stored state into a `NodeResult`. A request addressed to an unregistered worker fails the node immediately so a typo surfaces as a visible error; the node can opt into `WaitForWorker` to park until the worker registers instead.

Requests are claimed from the `WorkerRequests` table with the same `ROWLOCK, READPAST` pattern [`WorkClaimer`](Argent.Runtime/Workflows/Execution/WorkClaimer.cs) uses for work items, so two workers registered under the same name never run the same request. A claim starts a lease that the worker renews, and a lease that lapses requeues the request; the author's timeout is a separate hard budget that a renewal cannot extend, so a stuck worker cannot park a token forever. [`WorkerMaintenanceService`](Argent.Runtime/Workers/WorkerMaintenanceService.cs) runs in the engine host and sweeps expired leases, exhausted attempts, and silent workers every fifteen seconds. Delivery is at-least-once and each request id is stable across attempts, so handlers that are not naturally idempotent should record it.

Workers are external processes, so they authenticate with a bearer API key rather than an interactive session. Workers cannot register themselves: an administrator provisions one under `/Admin/Workers`, which generates the key, shows it exactly once, and stores only its SHA-256 hash. The client has no way to obtain a key, so a worker name cannot be claimed by whoever registers it first. Provisioning refuses a name that is already taken.

The same page issues three lifecycle operations. **Revoke** flips a worker to `Disabled`, which stops the key authenticating while keeping the registration and its history visible, and is reversible. **Rotate key** issues a replacement and invalidates the previous key immediately, forcing a client to be reconfigured. **Delete** removes the registration but deliberately keeps the requests it ran, because those record what a running workflow instance was told.

Without a registration call, the heartbeat is the only place a client can describe itself, so [`WorkerApi`](Argent.Web/Api/WorkerApi.cs) accepts the runtime and subject list there. The runtime is a free-form string such as `"Argent Python Worker 0.1"` rather than an enum: nothing routes on it, and a client library should be free to record a version, a build, or a host. It is null until the client first reports, which is how an administrator distinguishes a provisioned worker from one that has never connected. `GET /api/workers/me` lets a client confirm its key at startup rather than discovering a problem from a claim that never returns anything. `IWorkerTransport` in `Argent.Core` marks the outbound half of the contract so a broker-backed transport can replace the SQL queue without changing the node handler or the clients. The wire contract is published as [`schemas/argent-worker-protocol.schema.json`](schemas/argent-worker-protocol.schema.json) and each client validates against it, which is what keeps the clients from drifting. Admin pages for workers and requests live under `/Admin/Workers` and `/Admin/WorkerRequests`.

Resume latency is bounded by the engine's five-second poll, because `WorkItemSignal` cannot cross a process boundary. This is the same latency a completed user task already has, so it is not a regression, but it has not been measured or tuned.

## User interface and styling

All visual styling comes from one token set, defined once in [`Argent.Web/Styles/app.css`](Argent.Web/Styles/app.css) and compiled by Tailwind CSS v4 into `wwwroot/css/app.css`. That file is the single source of truth for the palette, type scale, shape and elevation, and it is the only stylesheet markup should need to reach for beyond the primitives it defines.

### The token layer

The design rests on one idea: **the neutral ramp is theme-relative**. `--argent-400` is a dark, AA-compliant grey in light mode and a light, AA-compliant grey in dark mode, because the step means "N steps away from the page background" rather than a fixed swatch. Tailwind's `gray-50`…`gray-950` are re-pointed at that ramp through `@theme inline`, so `bg-surface`, `text-ink-muted` and `border-line` resolve against whichever theme is active. Two consequences matter:

- Markup does not need `dark:` variants for neutrals. They were removed, which is why the pair-collapsing migration was safe to run across the tree.
- A single step cannot be tuned for "dark text on white" and "light text on black" at once, so the ramp had to be re-derived rather than inherited. Steps 400 and 500 are the readable "muted" tones in both themes; that is what brought the pre-existing `text-gray-400`-on-white contrast failures (2.5:1) up to AA without touching a single call site.

Above the ramp sit semantic tokens (`--canvas`, `--surface`, `--line`, `--fg`, plus `--success`/`--danger`/`--warning`/`--info` families), and above those the accent. The tenant brand colour arrives per request as `--primary`, injected by `_Layout.cshtml` from the Branding settings; the accent layer derives `--accent-subtle`, `--accent-border` and `--accent-text` from it with `color-mix()`, so any brand colour produces a coherent set. The accent is deliberately restricted to links, primary actions, focus rings and active states. It is never a background for a whole region, which is what keeps a multi-tenant app reading as institutional rather than as a themed landing page.

### Primitives

Markup composes components, not utility strings: `.card`, `.page-header`, `.btn` (+ `-primary`/`-secondary`/`-ghost`/`-danger`/`-sm`), `.form-input`/`.form-select`/`.form-textarea`, `.table-wrap`, `.badge`, `.alert`, `.dialog`, `.app-header`, `.empty-state`, and the `.btn-action*` family for low-emphasis row actions. The previous codebase carried its own hand-written copy of "what a primary button looks like" repeated with small variations across a dozen files; those are now one class.

### Stylesheets

| File | Contents |
| --- | --- |
| `Argent.Web/Styles/app.css` | Token layer, base layer, and every shared primitive. Compiled to `wwwroot/css/app.css`. |
| `wwwroot/css/modeler.css` | The workflow modeler. Defines `--md-*` aliases, but every value resolves to a shared token, so the modeler and the surrounding chrome stay in step. Node-type fills are a deliberate categorical palette and are the one place raw colours remain. |
| `wwwroot/css/designer.css` | Form and domain object designer surfaces, plus the shared `.property-section`/`.property-label`/`.input-group` blocks. These were inline `<style>` blocks in `FormDesigner.razor` and `DomainObjectDesigner.razor`; because Razor class libraries have no CSS isolation they leaked globally, `.property-section` and `.property-label` were defined twice with different values, and child components such as `FormPropertyPanel` were unstyled unless their parent happened to be on the page. |

`wwwroot/css/site.css` has been removed. It duplicated component classes that also existed in the Tailwind layer (with different sizes, so whichever loaded second won), carried a global `svg { background: #1f2937 }` that put a dark box behind every icon, and held a second, dead copy of the modeler styles.

`Argent.FormRuntime` renders `<argent-form>` with `shadow: 'none'`, so it inherits `app.css` and reads its palette through a `--argent-*` contract. `app.css` maps that contract onto the shared tokens; the component's own fallbacks only apply when it is used standalone. The dark-mode override that used to live in `site.css` is gone with it, which is why the mapping is now defined for both themes.

### Conventions

- Icons-only controls carry both `title` and `aria-label`; decorative icons are `aria-hidden`.
- Focus is a single 2px accent outline with a 2px offset, defined once in `@layer base`.
- `prefers-reduced-motion: reduce` collapses all transitions and animations.
- Tables use `font-variant-numeric: tabular-nums` and sticky headers under `.table-wrap-scroll`.
- The application header is dark in both themes, driven by its own `--shell-*` tokens rather than the neutral ramp, which flips with the theme.

### Migration tooling

[`tools/`](tools) holds the one-off scripts used for the rework, plus
[`verify_migration.py`](tools/verify_migration.py), which is the one worth
keeping. It compares every `class` attribute in the working tree against `HEAD`
and fails if any Razor expression inside one changed. Razor values may embed
`@("...")`, and token-level rewriting is unsafe there because `?`, `:`, `""`,
`==` and `is` legitimately repeat. That check is what caught a migration that
had silently rewritten `_sidebarTab == "props"` to `_sidebarTab ="props"`. The
other scripts refuse to touch a value containing `@(` and are idempotent, so
re-running them is harmless.

## Cross-cutting concerns

ASP.NET Core Identity handles user login and roles. Authorization also has group, ownership, policy, and capability services in `Argent.Runtime/Authorization`; web policies and handlers live in `Argent.Web/Authorization`. The web app supports English and Swedish localization. Audit entries are written for workflow and task events. ASP.NET Core Data Protection keys are persisted in the database so the web and engine can share protected data.
