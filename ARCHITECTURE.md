# Argent Architecture

## Solution Overview

Argent is a **no-code/low-code workflow and forms automation platform** built on .NET 10. Administrators define domain object schemas, dynamic forms, BPMN-like workflows, and external data source connections, all through browser-based designers. A background engine executes workflows, managing work items and concurrency.

**Runtime:** ASP.NET Core (Blazor Interactive Server)  
**Database:** SQL Server via Entity Framework Core  
**Orchestration:** .NET Aspire

---

## Dependency Graph

```
Argent.Models  (zero dependencies)
     ↑
Argent.Contracts ────→ Argent.Models
     ↑                       ↑
Argent.Infrastructure ──→ Argent.Models
     ↑                       ↑
     └─────────┬─────────────┘
               ↑
Argent.Runtime ──→ Argent.Contracts, Argent.Models, Argent.Infrastructure
     ↑
     ├───────────────────────────┐
     ↑                           ↑
Argent.Web ──→ Argent.Models, Argent.Infrastructure, Argent.Runtime, Argent.WebComponents
     ↑
Argent.WebComponents ──→ Argent.Contracts, Argent.Models, Argent.Runtime
     ↑
Argent.Host ──→ Argent.Web
```

---

## 1. Argent.Models — Domain Models Layer

**Dependencies:** None

The leaf-node assembly. Contains no logic — only POCOs, enums, JSON-polymorphic types, serializable definitions, and metadata attributes. Every other project depends on it.

### Namespaces & Key Types

| Namespace | Types | Purpose |
|---|---|---|
| `DomainObjects` | `DomainObject`, `DomainObjectDefinition`, `DomainObjectDraft`, `DomainObjectVersion`, `DomainObjectRecord`, `DomainProperty`, `DomainPropertyType`, `DomainChoiceOption`, `DomainDataSource`, `DomainObjectState` | Schema definitions for custom entity types, their typed properties, data source bindings, version lifecycle (draft → publish), and instance records |
| `DomainObjects.Querying` | `DomainQuery`, `DomainQueryResult`, `DomainFilter`, `DomainSort`, `DomainFilterCondition`, `DomainFilterOperator`, `DomainFilterLogic` | Declarative query/filter/sort/pagination model for domain object records |
| `Workflows` | `WorkflowDefinition`, `Workflow`, `WorkflowVersion`, `WorkflowDraft`, `NodeBase`, `Connection`, `StartEvent`, `EndEvent`, `ExclusiveGateway`, `InclusiveGateway`, `ParallelGateway`, `Lane`, `Pool`, `Token`, `WorkflowMetadata`, `WorkflowDiff` | Workflow graph model. `NodeBase` is the JSON-polymorphic base; concrete nodes carry `[WorkflowCanvasElement]` attributes with metadata for the designer. `Connection.Expression` carries the gateway condition (NCalc) for conditional branches |
| `Workflows.Activities` | `Activity`, `SQLActivity`, `RestActivity`, `JintActivity`, `UserActivity`, `ServerActivity`, `UserExperience` (`RedirectExperience`, `WaitExperience`, `TaskExperience`) | Concrete node types, each `NodeBase` subtypes discriminated by JSON type discriminator. `UserActivity.UX` selects how a user task is surfaced |
| `Workflows.Execution` | `WorkflowToken`, `WorkItem`, `WorkflowInstance`, `UserTask`, `WorkItemState`, `InstanceState`, `TokenState`, `UserTaskState` | Token-based execution state. `WorkflowToken` is the BPMN "token" that flows through the graph (carries the variable `Payload` plus `GroupId`/`TokenCount` for gateway join correlation); `WorkItem` is the claimable execution queue row (state, lock fields, retry counters, priority/schedule); `WorkflowInstance` tracks a running execution; `UserTask` is a pending human task |
| `Workflows.Auditing` | `WorkflowJournalEntry`, enums | Audit trail entries for workflow execution |
| `Workflows.Modeler` | `NodeLayout`, `LayoutElement` | Visual layout data (position, dimensions) for the workflow designer canvas |
| `Forms.Components` | `FormDefinition` | Top-level form definition containing a tree of `FormComponent` items |
| `Forms.Components.Base` | `FormComponent` (abstract, JSON-polymorphic), `FormField`, `FormLayout`, `SelectOption` | Form component tree. `FormField` for data-bound inputs, `FormLayout` for containers (Row, Column, Flex, Fieldset, Tabs, Accordion) |
| `Forms.Components.Configuration` | `FieldValidator` (abstract hierarchy: `LengthValidator`, `RangeValidator`, `RegexValidator`, `EmailValidator`, `UrlValidator`, `CompareFieldValidator`, `ExpressionValidator`); `Condition` (abstract: `AndCondition`, `OrCondition`, `NotCondition`, `CompareCondition`, `RoleCondition`, `ExpressionCondition`); `DataProviderConfig` | Declarative validation rules and conditional logic (visibility, required, disabled, readOnly) |
| `Forms.Filtering` | `QueryGroup`, `FilterItem`, `IFilter` | Generic filtering constructs for form data sources |
| `DataSources` | `DataSource` (abstract, JSON-polymorphic), `SqlDataSource`, `RestDataSource`, `SoapDataSource`, `DataSourceRequest`, `SqlRequest`, `RestRequest`, `SoapRequest`, `DataSourceResult`, `DataSourceTestResult`, `DataSourceSummary`, enums | Connection configuration models for SQL/REST/SOAP endpoints |
| `Identity` | `Person` (abstract), `InternalUser`, `ExternalUser`, `Organization`, `Position`, enums | Identity model extending ASP.NET Core Identity's `IdentityUser<Guid>` |
| `Attributes` | `WorkflowCanvasElementAttribute`, `NodePropertyAttribute` | Metadata attributes decorating workflow node classes for the designer toolbox |
| `Enums` | `WorkflowDefinitionState` (Draft, Published, Deployed) | Lifecycle states for workflows and domain objects |

### Polymorphic JSON serialization

All three designer subsystems (workflows, forms, domain objects) store their definitions as `nvarchar(max)` JSON columns. Polymorphism uses `System.Text.Json` type discriminators:

- `NodeBase` → `SQLActivity`, `RestActivity`, `JintActivity`, `UserActivity`, `ServerActivity`, `StartEvent`, `EndEvent`, `ExclusiveGateway`, `InclusiveGateway`, `ParallelGateway`
- `FormComponent` → `FormField`, `FormLayout` (with `LayoutType` sub-discriminator)
- `DataSource` → `SqlDataSource`, `RestDataSource`, `SoapDataSource`
- `Condition` → `AndCondition`, `OrCondition`, `NotCondition`, `CompareCondition`, `RoleCondition`, `ExpressionCondition`
- `FieldValidator` → `LengthValidator`, `RangeValidator`, `RegexValidator`, `EmailValidator`, `UrlValidator`, `CompareFieldValidator`, `ExpressionValidator`

---

## 2. Argent.Contracts — Service Abstractions

**Dependencies:** `Argent.Models`

Defines all public-facing interfaces and DTOs that decouple the runtime layer from consumers. No implementation lives here.

### Interfaces by Subsystem

#### Data Sources (`DataSources/`)

| Interface | Signature | Consumers | Purpose |
|---|---|---|---|
| `IDataSourceCatalog` | `GetSummariesAsync()`, `GetAsync(id)`, `GetByKeyAsync(key)`, `SaveAsync(dataSource, id?, user?)`, `DeleteAsync(id)` | Admin Razor Pages, DataSourceRunner | Admin CRUD for data source connections. Secrets encrypted at rest via `ISecretProtector` |
| `IDataSourceRunner` | `ExecuteAsync(dataSourceKey, request, parameters?, ct)`, `TestAsync(dataSourceKey, ct)`, `TestAsync(dataSource, ct)` | DomainObjectStore, forms, workflow engine | Single entry point for executing requests against stored data sources. Resolves by key, dispatches to registered provider by `DataSourceKind` |
| `IDataSourceProvider` | `Kind { get; }`, `ExecuteAsync(dataSource, request, parameters, ct)`, `TestAsync(dataSource, ct)` | DataSourceRunner | Stateless provider for one connection kind (SQL/REST/SOAP). Registered per-kind in DI |
| `ISecretProtector` | `Protect(plaintext)`, `Unprotect(ciphertext)` | DataSourceCatalog | Encryption abstraction allowing runtime to stay free of ASP.NET Data Protection dependency |

#### Domain Objects (`DomainObjects/`)

| Interface | Signature | Consumers | Purpose |
|---|---|---|---|
| `IDomainObjectDefinitionService` | `GetSummariesAsync()`, `GetAsync(id)`, `CreateAsync(key, name, ...)`, `GetWorkingDefinitionAsync(id)`, `GetPublishedDefinitionAsync(key)`, `SaveDraftAsync(id, def, user?)`, `PublishAsync(id, user?)`, `GetVersionsAsync(id)` | Admin Razor Pages, DomainObjectDesigner | Design-time authoring of domain object schemas with draft/version/publish lifecycle |
| `IDomainObjectStore` | `GetAsync(objectKey, id)`, `QueryAsync(objectKey, query?)`, `CreateAsync(objectKey, values, user?)`, `UpdateAsync(objectKey, id, values, user?)`, `UpsertAsync(objectKey, record, user?)`, `DeleteAsync(objectKey, id)`, `GetOptionsAsync(objectKey, valueField, labelField, ...)`, `QueryDataSourceAsync(objectKey, dataSourceIndex, query?)` | Forms (grids/dropdowns), workflow engine | Runtime CRUD + in-memory query pipeline for domain object instance records |

#### Forms (`Forms/`)

| Interface | Signature | Consumers | Purpose |
|---|---|---|---|
| `IFormContext` | `GetValue<T>(key)`, `GetValue(key)`, `SetValue(key, value)`, `GetAllData()`, `GetAllValues()`, `IsVisible(component)`, `IsRequired(field)`, `GetErrors(field)`, `Environment { get; }`, `UserRoles { get; }`, `OnStateChanged` event, `NotifyStateChanged()` | ArgentForm.razor, ConditionEvaluator, FormValidationService | Runtime per-form state bag: values, visibility, errors, touch tracking, reactivity |
| `IConditionEvaluator` | `Evaluate(condition, context)`, `EvaluateFieldVisible(field, context)`, `EvaluateFieldRequired(field, context)`, `EvaluateFieldDisabled(field, context)`, `EvaluateFieldReadOnly(field, context)` | ArgentFormContext, FormValidationService, DesignerFormContext | Evaluates condition trees (And/Or/Not/Compare/Role/NCalc Expression) against a form context |
| `IFormValidator` | `ValidateForm(definition, context)`, `ValidateField(field, context)` | ArgentFormContext, ArgentForm.razor | Validates visible fields against required flag, inline constraints, and registered validators |
| `IFormComponentRegistry` | `Resolve(typeName)`, `GetRegisteredTypes()` | ArgentRenderer.razor | Maps xtype strings to Blazor component Types. Seeded in Program.cs |

#### Workflows (`Workflows/`)

| Interface | Signature | Consumers | Purpose |
|---|---|---|---|
| `IWorkflowNodeRegistry` | `Resolve(name)`, `GetRegisteredTypes()`, `GetDescriptor(type)` | DesignerService, WorkflowModeler | Maps node type names to CLR types with `[WorkflowCanvasElement]` metadata descriptors |
| `IDesignerItem` | Marker interface | DesignerService | Marker for designer canvas items |

##### Execution (`Workflows/Execution/`)

The execution layer is a **token-based BPMN interpreter**. A `WorkflowToken` carries instance
state along a graph edge; a `WorkItem` is the claimable unit of work that advances one token
through one node.

| Interface | Signature | Consumers | Purpose |
|---|---|---|---|
| `IWorkClaimer` | `ClaimAsync(batchSize, ct)` → `IReadOnlyList<ClaimedWork>` | WorkflowEngine | Atomic, concurrency-safe batch claim of pending work items via raw skip-locked T-SQL. Returns `ClaimedWork` records |
| `ITokenRunner` | `RunAsync(claimed, ct)` | WorkflowEngine | Per-work-item orchestrator: resolves the node handler, builds the execution context, applies the result, and commits token movement |
| `ITokenMovement` | `CommitAsync(request, ct)` | TokenRunner | Transactionally consumes the input token and creates the next token(s) + work item(s). Takes a `TokenMovementRequest` (with `Targets`, optional `JournalEntry`, `IsTerminal`) |
| `INodeHandler` | `HandledNodeType { get; }`, `ExecuteAsync(node, ctx, ct)` → `NodeResult` | TokenRunner (resolved as `IEnumerable<INodeHandler>`, matched by `HandledNodeType`) | One handler per node type. Returns a `NodeResult` (`Completed`/`Waiting`/`Failed`, optional output variables and explicit targets). Handlers never touch the DB |
| `ITokenExecutionContext` | `InstanceId`, `TokenId`, `NodeId`, `Variables`, `CandidateTargets`, `TokenGroupId`, `TokenCount` | INodeHandler implementations | Read-only per-token context passed to handlers. `CandidateTarget` is an outgoing edge (target node + optional expression) |
| `IVariableBag` | `Get<T>(key)`, `Get(key)`, `Set(key, value)`, `Snapshot()` | Handlers, gateway evaluators | Token-scoped variable store backed by the token `Payload` JSON |
| `IWorkflowInstanceManager` | `StartAsync(definitionId, variables, ct)`, `SuspendAsync`, `ResumeAsync`, `CancelAsync`, `GetStateAsync` → `InstanceSnapshot` | External triggers, admin/UI | Instance lifecycle. `StartAsync` seeds the initial StartEvent token + work item |
| `IUserTaskManager` | `CreateTaskAsync(...)`, `GetTaskByTokenAsync(tokenId, ct)`, `CompleteTaskAsync(taskId, completedBy, resultData, ct)` | UserActivityHandler, external task UI/API | User-task CRUD and completion. `CompleteTaskAsync` re-queues a work item to resume the waiting token |
| `IAuditService` | `RecordAsync(category, eventType, instanceId?, tokenId?, actor?, details?, ct)` | Engine, managers | Writes `WorkflowJournalEntry` audit rows (details serialized to JSON) |

**Supporting records:** `ClaimedWork`, `TokenMovementRequest`, `TokenTarget`, `CandidateTarget`,
`NodeResult` / `NodeResultType`, `InstanceSnapshot`.

> **Legacy (superseded, unregistered):** `IWorkRouter`, `IWorkItemRepository`, `IWorkItemHandler`,
> and `IWorkflowExecutionContext` (plus `WorkItemRepository`/`WorkflowExecutionContext`) remain in
> the tree from the pre-token engine but are no longer wired into DI. The active path is
> `IWorkClaimer` → `ITokenRunner` → `INodeHandler` → `ITokenMovement`.

#### Shared (`/`)

| Interface | Signature | Consumers | Purpose |
|---|---|---|---|
| `IDbService` | `InvokeQuery(query, ds)`, `InvokeQueryAsync(query, ds)` | — | Low-level SQL execution against a SqlDataSource |

### DTOs shared across the boundary

- `DataSourceResults.cs` — `DataSourceResult`, `DataSourceTestResult`
- `DomainValidation.cs` — `DomainValidationError`, `DomainValidationException`
- `DomainOption.cs` — `DomainOption` (label/value for dropdowns)
- `DomainObjectSummary.cs` — `DomainObjectSummary`
- `WorkflowListItemDto.cs` — `WorkflowListItemDto`
- `NodeTypeDescriptor.cs` / `PropertyDescriptor.cs` / `PropertyDataType` — Metadata descriptors for workflow node types

---

## 3. Argent.Infrastructure — Data Access & Persistence

**Dependencies:** `Argent.Models`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`

EF Core `DbContext` with entity configurations, persistence document types, and JSON serialization converters.

### Stubs

- `DatabaseManager.cs` — Empty class, no implementation. Skipped.

### Core: `ArgentDbContext`

Extends `IdentityDbContext<InternalUser, IdentityRole<Guid>, Guid>` and configures:

**DbSets:**

| DbSet | Entity | Purpose |
|---|---|---|
| `Positions` | `Position` | Person-position join table |
| `FormDocuments` | `FormDocument` | Persisted form definitions (JSON) |
| `WorkItems` | `WorkItem` | Workflow execution queue (claimable, lockable) |
| `WorkflowTokens` | `WorkflowToken` | BPMN tokens — per-instance execution state |
| `WorkflowInstances` | `WorkflowInstance` | Running workflow instances |
| `WorkflowJournalEntries` | `WorkflowJournalEntry` | Execution audit trail |
| `UserTasks` | `UserTask` | Pending/completed human tasks |
| `WorkflowDefinitions` | `Workflow` | Workflow metadata records |
| `WorkflowVersions` | `WorkflowVersion` | Immutable published/deployed versions |
| `WorkflowDrafts` | `WorkflowDraft` | Editable drafts (one per workflow) |
| `DomainObjects` | `DomainObject` | Domain object metadata records |
| `DomainObjectVersions` | `DomainObjectVersion` | Published domain object versions |
| `DomainObjectDrafts` | `DomainObjectDraft` | Editable domain object drafts |
| `DomainObjectRecords` | `DomainObjectRecord` | Domain object instance data (JSON values) |
| `DataSources` | `DataSourceDocument` | Data source connections (encrypted config) |

**Key configuration patterns:**

- All `*Definition` types → serialized as JSON `nvarchar(max)` via `HasConversion` with `JsonSerializer.Serialize/Deserialize`
- Version properties → stored as `string`, converted to/from `System.Version`
- Draft tables → unique index on workflow/object (one draft at a time)
- `DataSourceDocument.Config` → encrypted JSON string, no EF conversion (decryption happens in `DataSourceCatalog`)
- `InternalUser.ExtraAttributes` → JSON dictionary conversion
- **Execution-engine indexes** — filtered indexes tuned for the work-item claim query:
  `IX_WorkItems_Claim_Immediate` on `(State, Priority, CreatedAt)` filtered to
  `State = 0 AND ScheduledAt IS NULL`, and `IX_WorkItems_Scheduled` for deferred items; plus
  state indexes on `WorkflowTokens`, `WorkflowInstances`, `UserTasks`, and `WorkflowJournalEntries`.
  Schema is created via `EnsureCreated()` (no migrations yet)

### Entity Documents

- `FormDocument` — `Id`, `Name`, `Description`, `Definition` (JSON), `CreatedBy`, `CreatedAt`, `UpdatedAt`
- `DataSourceDocument` — `Id`, `Key` (unique), `Name`, `Description`, `Kind`, `Config` (encrypted JSON), `CreatedBy`, `CreatedAt`, `UpdatedAt`
- `DomainObjectRecord` — `Id`, `DomainObjectId` (FK), `DefinitionVersion`, `Values` (JSON dictionary), `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`

### Draft/Version Persistence Pattern

Both workflows and domain objects follow the identical pattern:
- A `*Draft` entity holds the working definition (one per parent, unique constraint)
- A `*Version` entity holds published snapshots (immutable, semver-stamped)
- Publishing moves the draft content to a new version and deletes the draft
- Deploying (workflows only) transitions a version from `Published` → `Deployed` state

---

## 4. Argent.Runtime — Core Business Logic

**Dependencies:** `Argent.Contracts`, `Argent.Models`, `Argent.Infrastructure`  
**NuGet:** `CoreCLR-NCalc`, `Jint`, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Hosting.Abstractions`

Implements all contracts from `Argent.Contracts`. Organized into subsystems:

### 4a. Data Sources (`DataSources/`)

#### `DataSourceCatalog` (implements `IDataSourceCatalog`)

**Dependencies:** `IDbContextFactory<ArgentDbContext>`, `IHttpContextAccessor`, `ISecretProtector`

CRUD over `DataSourceDocument` table:
- **Read:** Queries metadata columns (Key, Name, Kind) for summaries; decrypts `Config` for full `DataSource` objects
- **Write:** Serializes polymorphic `DataSource` → JSON → `ISecretProtector.Protect()` → stores in `Config` column
- **Key uniqueness** enforced at the application level before save
- **Current user** resolved from `IHttpContextAccessor`

**Communication:**
- Consumed by: Admin Razor Pages (list/create/edit data sources), `DataSourceRunner`
- Incoming: `ISecretProtector` for encryption/decryption, `ArgentDbContext` for persistence
- Outgoing: Returns `DataSource` (decrypted, ready to use) or `DataSourceSummary` lists

#### `DataSourceRunner` (implements `IDataSourceRunner`)

**Dependencies:** `IDataSourceCatalog`, `IEnumerable<IDataSourceProvider>`

Dispatcher:
1. Calls `IDataSourceCatalog.GetByKeyAsync(key)`
2. Looks up registered `IDataSourceProvider` by `dataSource.Kind`
3. Forwards `ExecuteAsync` / `TestAsync` to the matched provider

**Communication:**
- Consumed by: `DomainObjectStore.QueryDataSourceAsync()`, forms (dynamic data source lookups), workflow activities
- Incoming: `IDataSourceCatalog`, list of `IDataSourceProvider` instances (resolved from DI)
- Outgoing: `DataSourceResult` (rows + optional error)

#### `SqlDataSourceProvider` (implements `IDataSourceProvider`)

**Kind:** `DataSourceKind.Sql`

- Opens a `SqlConnection` using `SqlDataSource.ConnectionString`
- Merges static request params with runtime params (runtime overrides)
- Parameterizes SQL query via `@param` placeholders
- Returns all rows as `List<Dictionary<string, object?>>` with column names as keys
- Test: executes `SELECT 1`

#### `RestDataSourceProvider` (implements `IDataSourceProvider`)

**Kind:** `DataSourceKind.Rest`

**Dependencies:** `IHttpClientFactory`

- Builds URL from `BaseUrl` + path (token-substituted) + query string
- Supports Auth types: ApiKey (header), Basic, Bearer
- Applies default headers + per-request headers
- Token substitution (`{{name}}`) in path, headers, body via `TokenTemplate.Apply()`
- Parses response JSON; optional `RowsPath` uses `JsonRows.Parse` for nested extraction
- Returns raw body + parsed rows

#### `SoapDataSourceProvider` (implements `IDataSourceProvider`)

**Kind:** `DataSourceKind.Soap`

**Dependencies:** `IHttpClientFactory`

- Sends POST to `EndpointUrl` with SOAP envelope (token-substituted)
- Sets `SOAPAction` header
- Supports Basic auth
- Returns raw XML body (SOAP faults keep the Raw for inspection)

#### `TokenTemplate`

Utility for `{{name}}` replacement in template strings (URLs, headers, SOAP envelopes, SQL). Regex-based, case-sensitive.

### 4b. Domain Objects (`DomainObjects/`)

#### `DomainObjectDefinitionService` (implements `IDomainObjectDefinitionService`)

**Dependencies:** `IDbContextFactory<ArgentDbContext>`, `IHttpContextAccessor`

Design-time authoring lifecycle:
- **Create:** Creates a new `DomainObject` header + initial empty `DomainObjectDraft`
- **Read working:** Returns draft if exists, otherwise latest published snapshot
- **Read published:** Returns latest published definition by `key`
- **Save draft:** Creates or updates the draft, updates header `UpdatedOn`
- **Publish:** Promotes draft to new `DomainObjectVersion` with semver bump (minor), deletes draft
- **Version listing:** All versions ordered descending

**Communication:**
- Consumed by: Admin Razor Pages (domain object CRUD), DomainObjectDesigner.razor
- Incoming: `ArgentDbContext` for persistence
- Outgoing: `DomainObject`, `DomainObjectDefinition`, `DomainObjectVersion` instances

#### `DomainObjectStore` (implements `IDomainObjectStore`)

**Dependencies:** `IDbContextFactory<ArgentDbContext>`, `IHttpContextAccessor`, `IDataSourceRunner`

Runtime CRUD for domain object records:
- **CRUD:** Create/Read/Update/Delete against `DomainObjectRecord` table
- **Upsert:** Inserts when `Id` is unknown, updates otherwise (form submit path)
- **Query:** Loads all records for an object key, applies in-memory filter → sort → page pipeline
- **Options:** Projects records to `DomainOption` (label/value) for dropdowns; can read from external SQL data source
- **QueryDataSourceAsync:** Delegates to `IDataSourceRunner.ExecuteAsync` for external SQL data sources defined on the domain object definition, maps columns via `DomainDataSource.ColumnMappings`
- **Validation:** Calls `DomainRecordValidator.Validate()` before write
- **Uniqueness:** In-memory check on properties marked `Unique`
- **Value coercion:** `DomainValueCoercion.Coerce()` normalizes values to match `DomainPropertyType` (numeric strings → numbers, etc.)

**In-memory query pipeline (v1):**
1. Filter: recursive `DomainFilter` with conditions (Equals, NotEquals, >, <, Contains, StartsWith, EndsWith, In, NotIn, IsNull, IsNotNull) combined with And/Or logic
2. Sort: multi-property sorting with ascending/descending
3. Page: Skip + Take

**Communication:**
- Consumed by: Forms (ArgentForm.razor for grids/lookups), workflow activities, admin pages
- Incoming: `IDataSourceRunner` for external queries, `ArgentDbContext` for persistence
- Outgoing: `DomainRecord`, `DomainQueryResult`, `DomainOption`

#### `DomainRecordValidator`

Validates a value dictionary against a `DomainObjectDefinition`:
- Required properties must have non-null, non-empty values
- Uniqueness violations
- Returns `List<DomainValidationError>`

#### `DomainValueCoercion`

Coerces raw `JsonElement` / string values to match `DomainPropertyType`:
- Number → `double`
- Boolean → `true`/`false`
- DateTime → `DateTime` parsing
- Handles JSON element normalization (null, string, number, boolean, object, array)

### 4c. Forms (`Forms/`)

#### `ArgentFormContext` (implements `IFormContext`)

**Dependencies:** `IFormValidator`, `IConditionEvaluator`

Scoped per-form state:
- **Data dictionary:** `GetValue/SetValue` with change detection (no-op if value unchanged)
- **Touch tracking:** `SetValue` marks field as touched; pristine fields hide errors until `RevealAllErrors()` (submit)
- **Visibility/Required:** Delegates to `IConditionEvaluator`
- **Validation:** Delegates to `IFormValidator.ValidateField()` but only for touched/revealed fields
- **Environment:** Merged user context (roles, environment variables)
- **Reactivity:** `OnStateChanged` event + `NotifyStateChanged()` triggers Blazor re-render

**Communication:**
- Consumed by: `ArgentForm.razor` and all field components via cascading parameter
- Incoming: `IFormValidator` (singleton), `IConditionEvaluator` (singleton)
- Outgoing: `OnStateChanged` event

#### `FormValidationService` (implements `IFormValidator`)

**Dependencies:** `IConditionEvaluator`

Walks the form definition tree:
1. Enumerates all `FormField` instances (recursing into `FormLayout` containers)
2. Skips hidden/disabled fields
3. Checks required (via `IConditionEvaluator.EvaluateFieldRequired`)
4. Applies inline constraints: `MinLength`/`MaxLength`, `Min`/`Max` values
5. Evaluates registered validators: `LengthValidator`, `RangeValidator`, `RegexValidator`, `EmailValidator`, `UrlValidator`, `CompareFieldValidator`, `ExpressionValidator`
6. Each validator can have a `When` condition for conditional application

**Communication:**
- Consumed by: `ArgentFormContext`, `ArgentForm.razor` (submit)
- Incoming: `IConditionEvaluator`
- Outgoing: Error dictionaries/list

#### `ConditionEvaluator` (implements `IConditionEvaluator`)

Evaluates condition trees against `IFormContext`:
- **Composite conditions:** `AndCondition` (all), `OrCondition` (any), `NotCondition` (negation)
- **CompareCondition:** Field value vs. another field value vs. literal; operators: `==`, `!=`, `>`, `<`, `>=`, `<=`, `contains`, `startsWith`, `endsWith`, `in`, `notIn`, `isEmpty`, `isNotEmpty`
- **RoleCondition:** Checks user roles against included/excluded role lists
- **ExpressionCondition:** Evaluates NCalc expressions with all form values as parameters
- **Field state shortcuts:** `EvaluateFieldVisible`, `EvaluateFieldRequired`, `EvaluateFieldDisabled`, `EvaluateFieldReadOnly`

#### `ArgentFormComponentRegistry` (implements `IFormComponentRegistry`)

Maps xtype strings → Blazor component `Type`. Pre-populated in `Program.cs`:
- `Row` → `ArgentRow`, `Column` → `ArgentColumn`, `Flex` → `ArgentFlex`
- `Fieldset` → `ArgentFieldset`, `Tabs` → `ArgentTabs`, `Accordion` → `ArgentAccordion`
- `HtmlBox` → `ArgentHtml`
- `TextField` → `ArgentText`, `DropdownField` → `ArgentDropdown`, `NumericField` → `ArgentNumeric`, `CheckboxField` → `ArgentCheckbox`

### 4d. Form Designer (`Forms/Modeling/`)

#### `FormDesignerService`

**Dependencies:** `IDbContextFactory<ArgentDbContext>`, `IHttpContextAccessor`

Stateful service for the form designer. One instance per session (scoped).

**State:**
- `Definition` — the live `FormDefinition` being edited
- `SelectedComponent` — currently selected component
- Drag state: `IsDragging`, `_dragPayload` ("add:xtype" or "move:componentId"), `Hover` (drop target)
- `StoredFormId`, `Name`, `Description`, `HasUnsavedChanges`

**Operations:**
- **Tree manipulation:** Add, Remove, Duplicate, MoveUp/MoveDown, reorder via drag-drop
- **Drag & drop:** `BeginDragAdd` (from toolbox), `BeginDragMove` (reorder), `SetHover`/`ClearHover` (target highlighting), `Drop` (execute drop)
- **Drop validation:** `CanDrop` prevents dropping a container into itself
- **Deep clone on duplicate:** `CloneComponent` via JSON serialize/deserialize, `RegenerateIdsAndNames`
- **Name uniqueness:** `UniqueFieldName` appends `_2`, `_3`, etc. to avoid collisions
- **Persistence:** `LoadAsync(id)` / `SaveAsync()` round-trips through `FormDocument` table

**Communication:**
- Consumed by: `FormDesigner.razor`
- Incoming: `ArgentDbContext` for persistence
- Outgoing: `OnChange` event

### 4e. Workflows (`Workflows/`)

#### `ArgentWorkflowNodeRegistry` (implements `IWorkflowNodeRegistry`)

Singleton. Scans known node types via reflection for `[WorkflowCanvasElement]` attributes:

| Node Type | Display | Category | Shape |
|---|---|---|---|
| `StartEvent` | Start | Events | Circle |
| `EndEvent` | End | Events | Circle |
| `SQLActivity` | SQL Query | Activities | Rectangle |
| `JintActivity` | Jint Script | Activities | Rectangle |
| `RestActivity` | REST Call | Activities | Rectangle |
| `UserActivity` | User Task | Activities | Rectangle |
| `InclusiveGateway` | Inclusive Gateway | Gateways | Diamond |
| `ExclusiveGateway` | Exclusive Gateway | Gateways | Diamond |
| `ParallelGateway` | Parallel Gateway | Gateways | Diamond |

Each is decorated with `[WorkflowCanvasElement]` and `[NodeProperty]` attributes that control designer behavior (icon, category, shape, default size, property editor).

#### `DesignerService` — Workflow Designer Service

**Dependencies:** `IHttpContextAccessor`, `IDbContextFactory<ArgentDbContext>`, `IWorkflowNodeRegistry`

Stateful (scoped) service for the workflow modeler.

**State:**
- `Nodes` / `Connections` — canvas view models
- `SelectedNode` / `SelectedConnection`
- `CurrentWorkflowId`, `CurrentWorkflowName`, `CurrentWorkflowDescription`
- `LoadedDraftId` / `LoadedVersionId` — tracks which version is being edited
- `IsReadOnlyVersion` — true when viewing a published/deployed version
- `CompiledDefinition` / `CompiledJson` / `ValidationResult`
- `HasUnsavedChanges` + `OnChange` event

**Lifecycle operations:**
1. **LoadWorkflowAsync(id):** Loads workflow header, prefers draft (editable), falls back to deployed → latest version (read-only)
2. **LoadVersionAsync(id):** Loads a specific version for viewing
3. **LoadDefinition(def):** Deserializes `WorkflowDefinition` into `DesignerNode`/`DesignerConnection` view models, caches metadata from `IWorkflowNodeRegistry`, auto-routes connections
4. **Compile():** Builds `WorkflowDefinition` from canvas state (nodes + connections + layouts), runs `WorkflowValidator`
5. **SaveDraft():** Upserts the Workflow header and draft record in a single transaction
6. **PublishVersion(isMajor):** Bumps semver, creates `WorkflowVersion`, deletes draft
7. **DeployVersion(id):** Un-deploys previous versions, marks new version as Deployed
8. **CreateDraftFromVersion(id):** Creates an editable draft from a published version
9. **DiscardDraft():** Deletes draft, falls back to version view
10. **Canvas operations:** `AddNode`, `AddConnection`, `DeleteSelected`, `Select`, `DeselectAll`, `Notify`, `MarkDirty`

**Communication:**
- Consumed by: `WorkflowModeler.razor`
- Incoming: `IWorkflowNodeRegistry` (node type metadata), `ArgentDbContext` (persistence)
- Outgoing: `OnChange` event, canvas state for rendering

#### Workflow Execution — Token-Based Interpreter

The engine models execution as BPMN **tokens** flowing through the graph. Two tables carry the
state: `WorkflowToken` (where a token currently sits, plus its variable payload) and `WorkItem`
(the claimable queue entry that will advance one token through one node). A handler computes
*what* should happen at a node and returns a `NodeResult`; the engine performs *all* persistence
in a single transaction, so a crash mid-node simply leaves the input token in place for recovery.

##### `WorkflowEngine` — Background Service

**Dependencies:** `ILogger<WorkflowEngine>`, `IServiceProvider`, `RecoveryPass`

`BackgroundService` running: **recovery → loop(claim → dispatch) → graceful drain**.
1. **Startup:** runs `RecoveryPass` once before serving
2. **Interval:** polls every 1 second
3. **Claim:** `IWorkClaimer.ClaimAsync(50)` atomically claims a batch
4. **Concurrency limit:** `SemaphoreSlim(50)` — awaited before each dispatch
5. **Dispatch:** fire-and-forget `Task.Run` → `ITokenRunner.RunAsync`, releasing the semaphore in `finally`. The dispatch is passed `stoppingToken` so in-flight work observes shutdown
6. **Graceful shutdown:** waits up to 30s for in-flight items to drain

##### `WorkClaimer` (implements `IWorkClaimer`)

**Dependencies:** connection string (resolved from the `ArgentDbContext` at registration)

Atomic, concurrency-safe claim using **raw T-SQL** (bypasses EF) — a single
`UPDATE … OUTPUT` over a CTE with `WITH (ROWLOCK, READPAST)`. `READPAST` skips rows another
worker has locked, so multiple engine instances never claim the same item:

```sql
WITH claim_cte AS (
    SELECT TOP (@BatchSize) Id, TokenId, …, State, LockedBy, LockExpirationUtc
    FROM WorkItems WITH (ROWLOCK, READPAST)
    WHERE State = 0 AND (ScheduledAt IS NULL OR ScheduledAt <= GETUTCDATE())
    ORDER BY Priority DESC, CreatedAt)
UPDATE claim_cte SET State = 1, LockedBy = @Machine,
       LockExpirationUtc = DATEADD(MINUTE, 5, GETUTCDATE())
OUTPUT INSERTED.Id, INSERTED.TokenId, …;
```

> The columns being updated (`State`, `LockedBy`, `LockExpirationUtc`) must appear in the CTE's
> select list — SQL Server only lets a CTE update columns it projects. The table is `WorkItems`
> (the EF DbSet name), not `WorkItem`.

##### `TokenRunner` (implements `ITokenRunner`)

**Dependencies:** `IServiceScopeFactory`, `IWorkflowNodeRegistry`, `IDbContextFactory<ArgentDbContext>`, `ILogger`

Orchestrates one work item inside a fresh DI scope:
1. Loads the deployed `WorkflowVersion` and the target `NodeBase`; loads the current token
2. Skips already-consumed tokens (idempotent on duplicate delivery)
3. **Gateway JOIN detection** (merge nodes — see below)
4. Builds `ITokenExecutionContext` (variable bag from the token payload + candidate outgoing edges)
5. Resolves the `INodeHandler` by `HandledNodeType`, runs it under a **lock-heartbeat** that renews `LockExpirationUtc` every 2 minutes for long activities
6. Applies the `NodeResult`:
   - **Waiting** → work item set `Waiting` (e.g. a user task), token stays put
   - **Failed** → dead-lettered immediately (a deterministic/business failure is non-retryable; transient faults surface as exceptions and *are* retried)
   - **Completed** → `DetermineTargets` resolves explicit targets (gateways) or all outgoing edges, then `ITokenMovement.CommitAsync`
7. Exceptions → `HandleFailureAsync`: retry (`RetryCount < MaxRetries` → back to `Pending`) or dead-letter

##### `TokenMovement` (implements `ITokenMovement`)

**Dependencies:** `ArgentDbContext`

`CommitAsync` performs the whole node transition in **one transaction**: consume the input token,
create a token + work item per target, carry/allocate the gateway `GroupId`/`TokenCount`
correlation (a fork of >1 target starts a new group), write the optional journal entry, and — for
a **terminal** target (`IsTerminal`, i.e. an `EndEvent` with no further targets) — complete the
instance once no other active tokens remain. Variable payloads are normalized on read
(`DeserializePayload` unwraps `JsonElement` values to native CLR types so NCalc conditions and
handlers see real numbers/strings/bools).

##### Gateway join (token correlation)

When a fork (parallel/inclusive split) produces N tokens they share a `GroupId` and `TokenCount = N`.
At a merge node `TokenRunner.ResolveJoinArrivalAsync` runs under a **serializable transaction**
(with bounded retry on lock contention) so exactly one sibling — the final arrival — fires the
join; the earlier arrivals are consumed and wait. This prevents both the *stall* race (every
sibling thinks it isn't last) and the *double-fire* race (two siblings both fire).

##### `WorkflowInstanceManager` (implements `IWorkflowInstanceManager`)

**Dependencies:** `ArgentDbContext`

Lifecycle: `StartAsync` seeds the StartEvent token + work item in a transaction; `Suspend`/`Resume`
toggle instance state; `Cancel` consumes active tokens and fails pending work items;
`GetStateAsync` returns an `InstanceSnapshot` with a **live** active-token count (computed, not
stored).

##### `RecoveryPass`

**Dependencies:** `IDbContextFactory<ArgentDbContext>`, `ILogger`

Runs at startup: (1) release stale locks (`LockExpirationUtc < now`) back to `Pending` with a
retry bump, dead-lettering those past `MaxRetries`; (2) re-queue orphan `Ready` tokens that have
no work item (consuming tokens whose instance is gone); (3) **flag** — but no longer silently
complete — `Running` instances that have zero active tokens (a stalled join would otherwise be
masked as success), logging them for inspection.

##### `UserTaskManager` (implements `IUserTaskManager`)

Creates `UserTask` rows when a `UserActivity` first executes; `CompleteTaskAsync` marks the task
done and enqueues a fresh work item so the waiting token resumes.

##### `AuditService` (implements `IAuditService`) & `WorkflowMeter`

`AuditService.RecordAsync` writes `WorkflowJournalEntry` rows (details JSON-serialized).
`WorkflowMeter` exposes OpenTelemetry instruments under meter `Argent.WorkflowEngine`:
`ItemsClaimed`, `TokensMoved`, `HandlerDurationMs`.

##### Node Handlers (`Workflows/Handlers/`)

One `INodeHandler` per node type, registered as `IEnumerable<INodeHandler>` and matched by
`HandledNodeType`:

| Handler | Node | Behavior |
|---|---|---|
| `StartEventHandler` | `StartEvent` | Pass-through; flows to outgoing edges |
| `EndEventHandler` | `EndEvent` | Pass-through; no targets → instance completion check |
| `SQLActivityHandler` | `SQLActivity` | Runs SQL via `IDataSourceRunner`; outputs `result`/`rowCount` |
| `RestActivityHandler` | `RestActivity` | HTTP via `IHttpClientFactory`; `{{token}}` substitution in URL/headers/body; outputs `statusCode`/`responseBody`/`responseHeaders` |
| `JintActivityHandler` | `JintActivity` | Executes JS via Jint (30s/10MB limits); injects variables, captures `ReturnVariable` |
| `UserActivityHandler` | `UserActivity` | Creates a `UserTask` and returns `Waiting`; resumes when the task is completed |
| `ExclusiveGatewayEvaluator` | `ExclusiveGateway` | First matching connection expression wins; falls back to the default (null-expression) edge; no match → `Failed` |
| `InclusiveGatewayEvaluator` | `InclusiveGateway` | Activates every matching edge (one target each); default edge if none match |
| `ParallelGatewayEvaluator` | `ParallelGateway` | Unconditional fan-out to all outgoing edges (and acts as the join on the inbound side) |

Gateway conditions are NCalc expressions on `Connection.Expression`, evaluated against the token's
`IVariableBag`. A gateway with no viable path returns `NodeResult.Failed` (non-retryable).

### 4f. Data (`Data/`)

#### `DbService` (implements `IDbService`)

**Dependencies:** `IDbContextFactory<ArgentDbContext>`

Low-level SQL execution. Opens a new `SqlConnection` against the admin database (ArgentDB's connection string) and executes arbitrary queries.

### 4g. Routing (`Workflows/Modeling/Routing/`)

#### `AnchorService`

Computes connection anchor points:
- For each node shape (Rectangle, Diamond, Circle), calculates the 4 edge-midpoints (Top, Right, Bottom, Left)
- Determines best exit/entry direction based on relative node positions

#### `RoutingService`

Orthogonal auto-routing for workflow connections:
- Generates waypoints (L-shaped or Z-shaped paths) between source/target nodes
- SVG path data generation from waypoints
- Segment hit-testing for drag interaction
- Waypoint dragging constraints (preserves orthogonal segments)
- Midpoint waypoint insertion

### Stubs

- `JintExecutor.cs` — Empty class wrapping Jint `Engine`. No implementation.

---

## 5. Argent.Web — ASP.NET Core Host

**Dependencies:** `Argent.Models`, `Argent.Infrastructure`, `Argent.Runtime`, `Argent.WebComponents`

The composition root: configures DI, middleware, authentication, and hosts Blazor Interactive Server components.

### `Program.cs` — Composition Root

**Service Registration Summary:**

| Lifetime | Service | Implementation |
|---|---|---|
| Singleton | `IFormComponentRegistry` | `ArgentFormComponentRegistry` (pre-populated with all xtype mappings) |
| Singleton | `IConditionEvaluator` | `ConditionEvaluator` |
| Singleton | `IFormValidator` | `FormValidationService` |
| Singleton | `IWorkflowNodeRegistry` | `ArgentWorkflowNodeRegistry` |
| Scoped | `IFormContext` | `ArgentFormContext` (factory: injected with validator + condition evaluator) |
| Scoped | `IDataSourceCatalog` | `DataSourceCatalog` |
| Scoped | `IDataSourceRunner` | `DataSourceRunner` |
| Scoped | `IDataSourceProvider` (×3) | `SqlDataSourceProvider`, `RestDataSourceProvider`, `SoapDataSourceProvider` |
| Scoped | `ISecretProtector` | `DataProtectionSecretProtector` |
| Singleton | `IWorkClaimer` | `WorkClaimer` (built with the DB connection string) |
| Singleton | `ITokenRunner` | `TokenRunner` |
| Scoped | `ITokenMovement` | `TokenMovement` |
| Scoped | `IWorkflowInstanceManager` | `WorkflowInstanceManager` |
| Transient | `RecoveryPass` | `RecoveryPass` |
| Singleton | `IUserTaskManager` | `UserTaskManager` |
| Singleton | `IAuditService` | `AuditService` |
| Transient | `INodeHandler` (×9) | `StartEventHandler`, `EndEventHandler`, `ExclusiveGatewayEvaluator`, `InclusiveGatewayEvaluator`, `ParallelGatewayEvaluator`, `SQLActivityHandler`, `RestActivityHandler`, `JintActivityHandler`, `UserActivityHandler` |
| Scoped | `IDomainObjectDefinitionService` | `DomainObjectDefinitionService` |
| Scoped | `IDomainObjectStore` | `DomainObjectStore` |
| Scoped | `DesignerService` | `DesignerService` |
| Scoped | `FormDesignerService` | `FormDesignerService` |
| Scoped | `DomainObjectDesignerService` | `DomainObjectDesignerService` |
| Hosted | — | `WorkflowEngine` |

**Infrastructure:**
- `DbContextFactory<ArgentDbContext>` + scoped fallback for Identity (`AddScoped(p => factory.CreateDbContext())`)
- ASP.NET Core Identity with `InternalUser`, roles (`IdentityRole<Guid>`)
- Authorization policies: `UserAdminOnly`, `FlowAdminOnly`, `FormAdminOnly`, `SuperAdminOnly`
- SignalR for Blazor Server
- Data Protection for `ISecretProtector`
- `IHttpClientFactory` for REST/SOAP providers

**Startup:**
- `EnsureDeleted()` + `EnsureCreated()` (development mode)
- Seeds roles (SuperAdmin, UserAdmin, FormAdmin, FlowAdmin, User) and sample users
- Generates JSON Schema for `FormDefinition` at `Resources/form-schema.json`

### Razor Pages

| Area | Pages | Purpose |
|---|---|---|
| Dashboard | `Index` | Workflow/form counts, recent items |
| Auth | `Login`, `Logout`, `QuickLogin` | Authentication |
| Account | `Profile`, `Privacy`, `Error` | User-facing pages |
| DataSources | `Index`, `Create`, `Edit` | Data source connection management |
| DomainObjects | `Index`, `Create`, `Edit` | Domain object schema management |
| Forms | `Index`, `Create`, `Edit`, `Live` | Form management + live form rendering for end-users |
| Workflows | `Index`, `Model/{Edit,View}`, `Modeler` | Workflow management + modeler page |
| UserAdministration | `Index`, `List`, `Create`, `Edit`, `Delete` | User admin CRUD |

### `DataProtectionSecretProtector` (implements `ISecretProtector`)

Web-layer implementation using ASP.NET Core `IDataProtector` with purpose string `"Argent.DataSources.v1"`. Bridges the runtime layer (which has no ASP.NET dependency) to the `IDataProtectionProvider` infrastructure.

### `DbInitializer`

Seeds the database:
- Roles: `SuperAdmin`, `UserAdmin`, `FormAdmin`, `FlowAdmin`, `User`
- Users: "MultiTool" (FormAdmin + FlowAdmin), "Overlord" (SuperAdmin), "alexb" (SuperAdmin)

### Security Authorization

Defined in `ServiceCollectionExtensions.AddArgentSecurity()`:

| Policy | Allowed Roles |
|---|---|
| `UserAdminOnly` | `UserAdmin`, `SuperAdmin` |
| `FlowAdminOnly` | `FlowAdmin`, `SuperAdmin` |
| `FormAdminOnly` | `FormAdmin`, `SuperAdmin` |
| `SuperAdminOnly` | `SuperAdmin` |

---

## 6. Argent.WebComponents — Blazor Component Library

**Dependencies:** `Argent.Contracts`, `Argent.Models`, `Argent.Runtime`

Reusable Blazor Interactive Server components for all designers and the live form renderer.

### Form System

#### `ArgentForm.razor` — Live Form Renderer

The main form component:
- Accepts `FormDefinition` as parameter
- Creates scoped `IFormContext` via DI
- Renders the component tree via `ArgentRenderer`
- Handles submission: validates all fields via `IFormValidator.ValidateForm()`
- Raises `OnValidSubmit` event with serialized form data
- Raises `OnInvalidSubmit` with error dictionary

#### `ArgentRenderer.razor` — Dynamic Component Dispatcher

Recursive renderer:
- Inspects `FormComponent.Xtype`
- Looks up `IFormComponentRegistry.Resolve(xtype)` for the Blazor component `Type`
- Sets cascading `IFormContext` and component-specific parameters
- Unknown xtypes render as `ArgentUnknownComponent`

#### Field Components

- `ArgentText.razor` — Text input (TextField)
- `ArgentNumeric.razor` — Numeric input
- `ArgentDropdown.razor` — Dropdown/select
- `ArgentCheckbox.razor` — Checkbox

#### Layout Components

- `ArgentRow.razor` — Horizontal flex row
- `ArgentColumn.razor` — Vertical flex column
- `ArgentFlex.razor` — Generic flex container
- `ArgentFieldset.razor` — Fieldset with legend
- `ArgentTabs.razor` — Tabbed container
- `ArgentAccordion.razor` — Accordion/collapse container
- `ArgentHtml.razor` — Raw HTML content display

#### Base Classes

- `FormLayoutComponentBase` — Base for layout components
- `FormInputComponentBase` — Base for input components (value binding, validation display)
- `DesignerFormContext` — Special `IFormContext` implementation for the designer preview (no real data, always pristine)

### Form Designer

#### `FormDesigner.razor`

Full drag-and-drop form builder:
- **Toolbox:** Categorized items (Fields, Layout, Content) with icons
- **Canvas:** Tree of `DesignerNode` components with `DropZone` targets between/within items
- **Property panel:** `FormPropertyPanel` for editing selected component properties
- **Preview:** Live preview of the form via `ArgentForm`
- **Operations:** Add from toolbox (click or drag), move, duplicate, delete, reorder

#### Supporting Components

- `DesignerNode.razor` — Renders a component in the designer tree with drag handle, selection, context menu
- `DropZone.razor` — Visual drop target indicator between and inside components
- `FormPropertyPanel.razor` — Dynamic property editor for the selected component, showing relevant fields per xtype
- `ValidatorListEditor.razor` — Editor for validator rules on a field
- `ConditionBuilder.razor` — Visual condition builder for visibility/required/disabled/readOnly rules
- `MonacoEditor.razor` — Code editor (NCalc expressions, HTML content)

### Workflow Modeler

#### `WorkflowModeler.razor` (~1300 lines)

The main workflow graph editor on an SVG canvas:
- **Pan & Zoom:** Mouse wheel zoom, middle-click/middle-mouse pan
- **Node dragging:** Drag nodes on canvas, auto-update connections
- **Connection drawing:** Drag from anchor points on source node to anchor on target node
- **Auto-routing:** Orthogonal path generation via `RoutingService`
- **Waypoint editing:** Drag waypoints on connection paths, insert midpoints
- **Snap guides:** Alignment snapping while dragging nodes
- **Rubber-band selection:** Click-drag on empty canvas to select multiple nodes
- **Resize handles:** Resize nodes from edges/corners
- **Space tool:** Rearrange canvas space
- **Auto-save:** Periodically or on specific triggers

#### Supporting Components

- `ModelerSidebar.razor` — Version history list, node list, validation results
- `ModelerPropertiesPanel.razor` — Dynamic property editor for selected node (reads `[NodeProperty]` metadata from `IWorkflowNodeRegistry`)
- `WorkflowVersioningPanel.razor` — Publish/Deploy version management UI
- `JsonViewer.razor` — Compiled workflow JSON display
- `ErrorViewer.razor` — Validation error display

#### `ModelerSession.cs`

View model for canvas state:
- `Zoom`, `PanX`, `PanY` — transform state
- `CanvasWidth`, `CanvasHeight` — viewport dimensions
- `ViewBox` — computed SVG viewBox string
- `ScreenToWorld(clientX, clientY, rectLeft, rectTop)` — coordinate transform

### Domain Object Designer

#### `DomainObjectDesigner.razor`

Schema designer for domain object definitions:
- Add/edit/remove properties with type selection, constraints, choice options
- Configure data source bindings
- Manage version lifecycle (draft/publish)

### Data Source Editor

#### `DataSourceEditor.razor`

Configuration editor for data source connections:
- Type selection (SQL/REST/SOAP) with type-specific fields
- Connection test button
- Secrets are encrypted at rest

---

## 7. Argent.Host — .NET Aspire Orchestration

**Dependencies:** `Argent.Web`

The .NET Aspire AppHost for distributed application orchestration:

```csharp
// SQL Server container with persistent data volume
var sqlServer = builder.AddSqlServer("sql-server")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var appDb = sqlServer.AddDatabase("ArgentDB");

// References Argent.Web, waits for database readiness
builder.AddProject<Argent_Web>("Argent")
    .WithReference(appDb)
    .WaitFor(appDb);
```

**Configuration:**
- SQL Server container with persistent volume (`ContainerLifetime.Persistent`)
- Database name: `ArgentDB`
- Web project waits for database before starting
- Connection string auto-provisioned by Aspire

**Parameters:**
- Data volume persists across container restarts
- No custom configuration parameters exposed

---

## 8. Argent.Runtime.Tests

**Location:** `Argent.Runtime.Tests/` (`Argent.Runtime.Tests.csproj` → references `Argent.Runtime`)  
**Frameworks:** xUnit, Moq, EF Core SQLite + InMemory, `Testcontainers.MsSql`, `Xunit.SkippableFact`

Coverage centers on the token-based workflow engine, in three tiers:

**Unit (`Workflows/Handlers/`, `Workflows/Execution/`):**
- Gateway evaluators — `ExclusiveGatewayEvaluator`, `InclusiveGatewayEvaluator`, `ParallelGatewayEvaluator`
- Activity handlers — `SQLActivityHandler` (mock `IDataSourceRunner`), `RestActivityHandler` (stub `HttpMessageHandler` asserting `{{token}}` substitution), `JintActivityHandler` (real Jint), `UserActivityHandler` (mock `IUserTaskManager`)
- `TokenRunner` (mocked deps: consumed-token short-circuit, missing-definition failure, success/commit, `Waiting`, exception-retry), `TokenMovement` helpers, `AuditService`, `RecoveryPass` (stale-lock release, dead-letter, orphan re-queue, flag-not-complete)

**Integration — SQLite (`IntegrationTestBase` + `WorkflowTraversalIntegrationTests`):**
in-memory SQLite with the real handlers and `TokenMovement`, advancing whole graphs:
linear flow, exclusive/inclusive condition routing, and parallel split→join. Fast inner loop; it
drives work via EF (`AdvanceAsync`) and therefore does **not** exercise the raw-T-SQL `WorkClaimer`.

**Integration — SQL Server (`[Trait("Category","Sql")]`, `[SkippableFact]`):**
`SqlServerFixture` spins up SQL Server via `Testcontainers.MsSql`. `WorkClaimerSqlServerTests`
exercises the real skip-locked claim query; `ParallelJoinConcurrencyTests` drives two sibling
tokens into a join on parallel tasks to assert the join fires exactly once. These caught two
SQL-only `WorkClaimer` bugs the SQLite tier could not. The fixture auto-detects a rootless
**Podman** socket (`$XDG_RUNTIME_DIR/podman/podman.sock`) when `DOCKER_HOST` is unset and disables
Ryuk; if no container runtime is present the SQL tests skip rather than fail.

---

## 9. Argent.WebComponents.Tests

**Status:** Small (1 test file)  
**Location:** `Argent.WebComponents.Tests/`  
**Dependencies:** None (standalone — no project references)

**Note:** This project has no project references. The tested `PathingTests` algorithms are copied inline rather than referencing the actual `Argent.WebComponents` or `Argent.Runtime` routing code.

**Tests:**

`PathingTests.cs` (20+ methods):
- `AutoRoute` — Orthogonal path generation between nodes at various relative positions
- `FindNearestSegment` — Hit-testing against connection waypoints
- `DragSegment` — Segment dragging constraints
- `DragWaypoint` — Waypoint movement constraints
- `InsertWaypointAtMidpoint` — Midpoint waypoint insertion

---

## Communication Patterns Summary

### Inter-Project Communication

| Producer | Consumer(s) | Mechanism |
|---|---|---|
| `Argent.Models` | All other projects | NuGet package reference; types used directly |
| `Argent.Contracts` | `Argent.Runtime` (implements), `Argent.Web`/`Argent.WebComponents` (consumes via DI) | Interface contracts; runtime registers implementations |
| `Argent.Infrastructure` | `Argent.Runtime` | Direct DbContext usage (via factory) |
| `Argent.Runtime` | `Argent.Web`, `Argent.WebComponents` | DI injection (behind contracts) |
| `Argent.Web` | `Argent.Runtime` through `ISecretProtector` bridge | Web provides ASP.NET-specific implementation; runtime consumes through abstraction |

### Intra-System Communication

| Pattern | Example | Mechanism |
|---|---|---|
| **DI Injection** | `DataSourceRunner` ← `IDataSourceCatalog` + `IDataSourceProvider[]` | Constructor injection, resolved by DI container |
| **Event/Notification** | `DesignerService.OnChange` → `WorkflowModeler.razor` | C# events; Blazor `StateHasChanged()` |
| **Background Polling** | `WorkflowEngine` claims work via `IWorkClaimer` | `BackgroundService`, 1-second poll, `SemaphoreSlim(50)` |
| **Fire-and-Forget Dispatch** | `WorkflowEngine` → `Task.Run` → `ITokenRunner.RunAsync` | `Task.Run` with separate DI scope, passed `stoppingToken` |
| **Skip-Locked Claim** | `WorkClaimer.ClaimAsync()` | Raw T-SQL `UPDATE … OUTPUT` with `(ROWLOCK, READPAST)` — safe across concurrent engines |
| **Serializable Join** | `TokenRunner.ResolveJoinArrivalAsync()` | Serializable transaction + bounded retry; exactly one sibling fires the merge |
| **Single-Transaction Movement** | `TokenMovement.CommitAsync()` | Consume input token + create targets + journal + completion in one transaction |
| **JSON Serialization** | Designer services serialize/deserialize definitions | `System.Text.Json` for persistence and cloning |
| **Cascading Parameter** | `ArgentForm` passes `IFormContext` to field components | Blazor `CascadingValue` |

### Data Flow: Form Submission

```
User fills form in browser
  → ArgentText/ArgentDropdown etc. call IFormContext.SetValue()
    → ArgentFormContext updates data dict, marks touched, fires OnStateChanged
      → ConditionEvaluator re-evaluates visibility/required on re-render
  → User clicks Submit
    → ArgentForm calls IFormValidator.ValidateForm()
      → FormValidationService walks all fields, evaluates conditions, checks validators
    → If valid: serializes FormData, raises OnValidSubmit
    → If invalid: calls IFormContext.RevealAllErrors(), shows errors
```

### Data Flow: Workflow Execution

```
User publishes/deploys workflow definition
  → DesignerService.PublishVersion() / DeployVersion()
    → Creates WorkflowVersion record in DB

IWorkflowInstanceManager.StartAsync()
  → Creates WorkflowInstance + initial StartEvent token + WorkItem (one transaction)

WorkflowEngine (BackgroundService, 1s interval)
  → RecoveryPass on startup (stale locks, orphan tokens, stuck-instance flags)
  → IWorkClaimer.ClaimAsync(50)        // skip-locked UPDATE … OUTPUT
    → For each ClaimedWork: Task.Run → ITokenRunner.RunAsync (new scope)
        → Load deployed WorkflowVersion + target NodeBase + current token
        → If merge node: ResolveJoinArrivalAsync (serializable) — wait or fire
        → Resolve INodeHandler by HandledNodeType, run with lock heartbeat
        → NodeResult:
            Waiting → WorkItem.Waiting (e.g. user task)
            Failed  → dead-letter (non-retryable) | exception → retry
            Completed → ITokenMovement.CommitAsync (one transaction):
                          consume input token, create next token(s)+WorkItem(s),
                          journal, complete instance if terminal EndEvent

UserTask completion
  → IUserTaskManager.CompleteTaskAsync() → enqueues a WorkItem → token resumes
```

### Data Flow: Data Source Execution

```
Form / DomainObjectStore / WorkflowActivity needs data
  → IDataSourceRunner.ExecuteAsync(dataSourceKey, request, params)
    → DataSourceRunner
      → IDataSourceCatalog.GetByKeyAsync(key) → decrypts from DB
      → Routes to IDataSourceProvider by DataSourceKind
        → SqlDataSourceProvider: SqlConnection → SqlCommand → rows
        → RestDataSourceProvider: HttpClient → REST call → parse JSON
        → SoapDataSourceProvider: HttpClient → SOAP envelope → raw XML
    → Returns DataSourceResult (rows + optional error)
```
