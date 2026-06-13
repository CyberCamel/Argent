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
| `Workflows` | `WorkflowDefinition`, `Workflow`, `WorkflowVersion`, `WorkflowDraft`, `NodeBase`, `Connection`, `StartEvent`, `EndEvent`, `ExclusiveGateway`, `InclusiveGateway`, `Lane`, `Pool`, `Token`, `WorkflowMetadata`, `WorkflowDiff` | Workflow graph model. `NodeBase` is the JSON-polymorphic base; concrete nodes carry `[WorkflowCanvasElement]` attributes with metadata for the designer |
| `Workflows.Activities` | `Activity`, `SQLActivity`, `RestActivity`, `JintActivity`, `UserActivity`, `ServerActivity` | Concrete node types, each `NodeBase` subtypes discriminated by JSON type discriminator |
| `Workflows.Execution` | `WorkItem`, `WorkflowInstance`, `ExecutionResult`, enums | Runtime execution state: work items (pending/locked/completed), instance tracking, handler results |
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

- `NodeBase` → `SQLActivity`, `RestActivity`, `JintActivity`, `UserActivity`, `ServerActivity`, `StartEvent`, `EndEvent`, `ExclusiveGateway`, `InclusiveGateway`
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

| Interface | Signature | Consumers | Purpose |
|---|---|---|---|
| `IWorkItemRepository` | `GetWorkAsync()`, `TryLockWorkItemAsync(id)`, `CompleteWorkItemAsync(id)`, `FreeWorkItemAsync(id)`, `CreateWorkItemAsync(workItem)` | WorkflowEngine, WorkRouter | EF Core persistence and optimistic locking of work items |
| `IWorkRouter` | `Dispatch(workItem, OnComplete)` | WorkflowEngine | Fire-and-forget dispatch of work items to handlers. Resolves latest deployed workflow version |
| `IWorkflowExecutionContext` | `Instance { get; }`, `Variables { get; }` | IWorkItemHandler implementations | Provides workflow instance and variable dictionary to handlers |
| `IWorkflowJournalManager` | `RecordEntry(entry)` | WorkRouter, handlers | Records audit trail entries during workflow execution |
| `IWorkItemHandler` | `HandleWorkItemAsync(workItem, ctx)` | WorkRouter | Handler contract for processing a dispatched work item |

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
| `WorkItems` | `WorkItem` | Workflow execution queue |
| `WorkflowInstances` | `WorkflowInstance` | Running workflow instances |
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

#### Workflow Execution

##### `WorkflowEngine` — Background Service

**Dependencies:** `ILogger<WorkflowEngine>`, `IServiceProvider`

`BackgroundService` that runs the execution loop:
1. **Interval:** Polls every 5 seconds
2. **Scope:** Creates a new DI scope per tick
3. **Fetch:** Calls `IWorkItemRepository.GetWorkAsync()` for all available work items
4. **Concurrency limit:** `SemaphoreSlim(50)` — skips items when max concurrency reached
5. **Lock:** Calls `TryLockWorkItemAsync` (optimistic EF Core locking with 5-min expiration)
6. **Dispatch:** Calls `IWorkRouter.Dispatch(workItem, releaseAction)` and releases semaphore on completion

**Communication:**
- Incoming: `IServiceProvider` for scoped service resolution
- Outgoing: `IWorkItemRepository` for fetch/lock, `IWorkRouter` for dispatch

##### `WorkRouter` (implements `IWorkRouter`)

**Dependencies:** `IServiceScopeFactory`, `ILogger<WorkRouter>`

Fire-and-forget dispatch (`Task.Run`):
1. Creates a new DI scope
2. Loads the latest deployed `WorkflowVersion` (falls back to latest version)
3. Finds the target `NodeBase` from the workflow definition by `WorkItem.NodeId`
4. Resolves `IWorkItemHandler` from DI
5. Creates `WorkflowExecutionContext` with `WorkflowInstance` + empty `Variables`
6. Calls `handler.HandleWorkItemAsync(workItem, ctx)`
7. Completes the work item on success/failure; frees on exception

**Communication:**
- Incoming: Dispatched from `WorkflowEngine`
- Outgoing: `IWorkItemRepository` (complete/free), `ArgentDbContext` (read workflow versions), `IWorkItemHandler` (execute)

##### `WorkItemRepository` (implements `IWorkItemRepository`)

**Dependencies:** `ArgentDbContext`

- **GetWorkAsync:** Returns all work items (no filtering — filtering deferred)
- **TryLockWorkItemAsync:** Optimistic lock via `ExecuteUpdateAsync` — only locks if not locked or lock expired (>5 min)
- **FreeWorkItemAsync:** Clears lock fields
- **CompleteWorkItemAsync:** Removes the work item from the queue
- **CreateWorkItemAsync:** Adds a new work item

##### `WorkflowExecutionContext` (implements `IWorkflowExecutionContext`)

Simple data holder: `WorkflowInstance` + `Variables` dictionary.

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
| Scoped | `IWorkItemRepository` | `WorkItemRepository` |
| Scoped | `IWorkRouter` | `WorkRouter` |
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

**Status:** Small (2 test files)  
**Location:** `Argent.Runtime.Tests/`  
**Project file:** `Argent.Logic.Tests.csproj` (references `Argent.Logic`, which does not exist — broken reference)

**Note:** The project file has a broken reference to `Argent.Logic` (stub project that was never created). The actual tests reference types from Argent.Runtime.

**Tests:**

`DesignerSessionTests.cs` (4 theories):
- `ScreenToWorld_NoPanNoZoom` — Identity transform
- `ScreenToWorld_WithPan` — Pan offset applied
- `ScreenToWorld_WithZoom` — Zoom scaling applied
- `ScreenToWorld_WithPanAndZoom` — Combined transform round-trip

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
| **Background Polling** | `WorkflowEngine` polls `IWorkItemRepository` | `BackgroundService` with 5-second `Task.Delay` |
| **Fire-and-Forget Dispatch** | `WorkRouter.Dispatch()` creates `Task.Run` | `Task.Run` with separate DI scope |
| **Optimistic Locking** | `WorkItemRepository.TryLockWorkItemAsync()` | EF Core `ExecuteUpdateAsync` with conditional WHERE |
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
  
External trigger or activity creates WorkItem
  → WorkItemRepository.CreateWorkItemAsync()

WorkflowEngine (BackgroundService, 5s interval)
  → WorkItemRepository.GetWorkAsync()
    → For each item: TryLockWorkItemAsync() → WorkRouter.Dispatch()
      → WorkRouter (Task.Run, new scope)
        → Loads latest deployed WorkflowVersion
        → Finds target NodeBase
        → Resolves IWorkItemHandler
        → Creates WorkflowExecutionContext
        → handler.HandleWorkItemAsync(workItem, ctx)
        → CompleteWorkItemAsync / FreeWorkItemAsync
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
