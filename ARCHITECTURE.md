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

`Argent.Runtime.Tests` and `Argent.WebComponents.Tests` cover runtime and UI logic. The TypeScript form runtime has its own tests and build in `Argent.FormRuntime`.

## Startup and persistence

[`Argent.Web/Program.cs`](Argent.Web/Program.cs) registers Razor Pages, interactive server and WebAssembly components, Identity, the designer and runtime APIs, and the service implementations. It applies EF migrations before serving requests. Development startup also runs [`DbInitializer`](Argent.Web/DbInitializer.cs) to seed users and sample domain data. [`Argent.Engine/Program.cs`](Argent.Engine/Program.cs) registers the same persistence and runtime services plus the hosted workflow engine. The Aspire host waits for the web app so migrations run before the engine starts.

[`ArgentDbContext`](Argent.Infrastructure/Data/ArgentDbContext.cs) stores Identity users and groups, policy documents, data sources, domain object definitions and records, form drafts/versions/submissions, workflow drafts/versions/instances, tokens, work items, user tasks, timers, audit entries, branding, and Data Protection keys. Migrations live in `Argent.Infrastructure/Migrations`. Both processes connect to the `ArgentDB` SQL Server database through [`AddArgentPersistence`](Argent.Runtime/DependencyInjection/PersistenceExtensions.cs).

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

## Cross-cutting concerns

ASP.NET Core Identity handles user login and roles. Authorization also has group, ownership, policy, and capability services in `Argent.Runtime/Authorization`; web policies and handlers live in `Argent.Web/Authorization`. The web app supports English and Swedish localization. Audit entries are written for workflow and task events. ASP.NET Core Data Protection keys are persisted in the database so the web and engine can share protected data.
