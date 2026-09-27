using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.BoundaryEvents;
using Argent.Core.Workflows.Designer;
using Argent.Core.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Editing;
using Argent.WebComponents.Workflows.Modeler.Interaction;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Xunit;

namespace Argent.WebComponents.Tests.Workflows.Modeler;

/// <summary>
/// A designer store that keeps everything in memory, so the designer can be exercised
/// without a database.
/// </summary>
internal sealed class FakeDesignerStore : IWorkflowDesignerStore
{
    public WorkflowDefinition? Definition { get; set; }
    public Guid DraftId { get; } = Guid.NewGuid();
    public Guid WorkflowId { get; } = Guid.NewGuid();
    public int SaveCount { get; private set; }
    public bool FailNextSave { get; set; }
    public WorkflowDefinition? LastSaved { get; private set; }

    public Task<WorkflowDesignerLoadResult?> LoadWorkflowAsync(Guid workflowId) =>
        Task.FromResult<WorkflowDesignerLoadResult?>(new()
        {
            WorkflowId = WorkflowId,
            Name = "Test",
            Definition = Definition,
            DraftId = DraftId
        });

    public Task<WorkflowDesignerLoadResult?> LoadVersionAsync(Guid versionId) =>
        Task.FromResult<WorkflowDesignerLoadResult?>(new()
        {
            WorkflowId = WorkflowId,
            Name = "Test",
            Definition = Definition,
            VersionId = versionId
        });

    public Task<WorkflowSaveDraftResult> SaveDraftAsync(WorkflowSaveDraftRequest request)
    {
        SaveCount++;
        LastSaved = request.Definition;
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("store unavailable");
        }
        Definition = request.Definition;
        return Task.FromResult(new WorkflowSaveDraftResult { WorkflowId = WorkflowId, DraftId = DraftId });
    }

    public Task<WorkflowPublishResult> PublishVersionAsync(WorkflowPublishRequest request) =>
        Task.FromResult(new WorkflowPublishResult
        {
            VersionId = Guid.NewGuid(),
            Definition = Definition,
            RoleAudiences = []
        });

    public Task<WorkflowDesignerLoadResult> DeployVersionAsync(Guid versionId, string? userId = null) =>
        Task.FromResult(new WorkflowDesignerLoadResult
        {
            WorkflowId = WorkflowId,
            Name = "Test",
            Definition = Definition,
            VersionId = versionId
        });

    public Task SaveRoleAudiencesAsync(Guid versionId, Dictionary<Guid, RoleAudience> audiences) => Task.CompletedTask;

    public Task<Dictionary<Guid, RoleAudience>?> GetVersionAudiencesAsync(Guid versionId) =>
        Task.FromResult<Dictionary<Guid, RoleAudience>?>(null);

    public Task<WorkflowSaveDraftResult> CreateDraftFromVersionAsync(Guid versionId, string? userId = null) =>
        Task.FromResult(new WorkflowSaveDraftResult { WorkflowId = WorkflowId, DraftId = DraftId });

    public Task<WorkflowDesignerLoadResult?> DiscardDraftAsync(Guid draftId, Guid workflowId) =>
        Task.FromResult<WorkflowDesignerLoadResult?>(null);

    public Task<WorkflowVersionTimeline?> GetVersionTimelineAsync(Guid workflowId) =>
        Task.FromResult<WorkflowVersionTimeline?>(null);

    public Task<WorkflowDiff?> GetDraftDiffAsync(Guid workflowId) =>
        Task.FromResult<WorkflowDiff?>(null);
}

internal sealed class TestRegistry : IWorkflowNodeRegistry
{
    public Type? Resolve(string name) => Type.GetType($"Argent.Core.Workflows.Activities.{name}");

    public IEnumerable<NodeTypeDescriptor> GetRegisteredTypes() =>
    [
        new()
        {
            NodeType = typeof(UserActivity),
            DisplayName = "Task",
            Icon = "person",
            Category = "Activities",
            Shape = NodeShape.Rectangle,
            DefaultWidth = 160,
            DefaultHeight = 80
        }
    ];

    public NodeTypeDescriptor? GetDescriptor(Type nodeType) =>
        GetRegisteredTypes().FirstOrDefault(d => d.NodeType == nodeType);
}

/// <summary>
/// Gesture lifecycle and undo: a completed gesture is one history entry, cancelling
/// restores geometry, routing and selection, and a click that moves nothing changes
/// nothing.
/// </summary>
public class InteractionTransactionTests
{
    private static (DesignerService State, GeometryEditor Editor, FakeDesignerStore Store) NewDesigner()
    {
        var store = new FakeDesignerStore();
        var state = new DesignerService(store, new TestRegistry());
        return (state, GeometryEditor.For(state), store);
    }

    private static DesignerNode AddNode(DesignerService state, double x, double y, string name = "n")
    {
        var node = new DesignerNode
        {
            NodeData = new UserActivity { Name = name },
            Title = name,
            Shape = NodeShape.Rectangle,
            X = x, Y = y, Width = 160, Height = 80
        };
        state.Nodes.Add(node);
        return node;
    }

    private static DesignerConnection AddConnection(DesignerService state, DesignerNode a, DesignerNode b)
    {
        var connection = new DesignerConnection
        {
            EngineConnection = new Connection { From = a.NodeData, To = b.NodeData },
            Source = a,
            Target = b
        };
        state.Connections.Add(connection);
        ElasticRouter.Route(connection, state.Nodes);
        return connection;
    }

    [Fact]
    public void ACompletedDragIsOneUndoEntry()
    {
        var (state, editor, _) = NewDesigner();
        var a = AddNode(state, 0, 100, "a");
        var b = AddNode(state, 400, 240, "b");
        AddConnection(state, a, b);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        editor.MoveNodes(gesture.Baseline, [a], 30, 20);
        gesture.Commit("Move nodes");

        Assert.True(state.History.CanUndo);
        Assert.Equal("Move nodes", state.History.UndoDescription);

        state.Undo();
        Assert.Equal(0, a.X);
        Assert.Equal(100, a.Y);

        state.Redo();
        Assert.Equal(30, a.X);
        Assert.Equal(120, a.Y);
    }

    [Fact]
    public void AClickWithoutMovementCreatesNoUndoEntryAndNoDirtyState()
    {
        var (state, editor, _) = NewDesigner();
        var a = AddNode(state, 0, 100);
        var b = AddNode(state, 400, 100);
        AddConnection(state, a, b);
        state.HasUnsavedChanges = false;

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        state.SelectNodes([a], a);
        gesture.Commit("Move nodes");

        Assert.False(state.History.CanUndo);
        Assert.False(state.HasUnsavedChanges);
    }

    [Fact]
    public void CancellingRestoresGeometryRoutingAndSelection()
    {
        var (state, editor, _) = NewDesigner();
        var a = AddNode(state, 0, 100, "a");
        var b = AddNode(state, 400, 240, "b");
        var connection = AddConnection(state, a, b);
        var originalWaypoints = connection.CopyWaypoints();
        state.SelectNodes([a], a);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        editor.MoveNodes(gesture.Baseline, [a], 500, 300);
        state.SelectNodes([b], b);
        Assert.NotEqual(0, a.X);
        gesture.Cancel();

        Assert.Equal(0, a.X);
        Assert.Equal(100, a.Y);
        Assert.Equal(originalWaypoints.Count, connection.Waypoints.Count);
        for (int i = 0; i < originalWaypoints.Count; i++)
        {
            Assert.Equal(originalWaypoints[i].X, connection.Waypoints[i].X, 6);
            Assert.Equal(originalWaypoints[i].Y, connection.Waypoints[i].Y, 6);
        }
        Assert.Contains(a, state.SelectedNodes);
        Assert.Same(a, state.SelectedNode);
        Assert.False(state.History.CanUndo);
    }

    [Fact]
    public async Task AutosaveIsSuspendedUntilTheGestureIsCommitted()
    {
        var (state, _, store) = NewDesigner();
        AddNode(state, 0, 100);
        state.HasUnsavedChanges = true;

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        Assert.False(state.CanAutoSave);
        Assert.False(await state.TryAutoSaveAsync(readOnly: false));
        Assert.Equal(0, store.SaveCount);

        gesture.Commit("Move nodes");
        Assert.True(state.CanAutoSave);
    }

    [Fact]
    public void RepeatedMovementsDoNotDrift()
    {
        var (state, editor, _) = NewDesigner();
        var a = AddNode(state, 0, 100);
        var b = AddNode(state, 400, 240);
        AddConnection(state, a, b);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        for (int i = 1; i <= 20; i++)
            editor.MoveNodes(gesture.Baseline, [a], 5 * i, 3 * i);
        gesture.Commit("Move nodes");

        Assert.Equal(100, a.X, 6);
        Assert.Equal(160, a.Y, 6);
    }

    [Fact]
    public void DraggingAnySelectedNodeMovesTheWholeSelectionAndPreservesRelativePositions()
    {
        var (state, editor, _) = NewDesigner();
        var a = AddNode(state, 0, 100, "a");
        var b = AddNode(state, 200, 100, "b");
        state.SelectNodes([a, b], b);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        // The pointer holds b, but the whole selection travels with it.
        editor.MoveNodes(gesture.Baseline, state.SelectedNodes.ToList(), 50, 25);
        gesture.CommitSilently();

        Assert.Equal(50, a.X);
        Assert.Equal(250, b.X);
        Assert.Equal(125, a.Y);
        Assert.Equal(125, b.Y);
    }

    [Fact]
    public void ABoundaryEventMovesExactlyOnceWithItsParent()
    {
        var (state, editor, _) = NewDesigner();
        var parent = AddNode(state, 0, 100, "parent");
        var child = new DesignerNode
        {
            NodeData = new TimerBoundaryEvent { ParentNodeId = parent.NodeData.Id },
            Title = "timer",
            Shape = NodeShape.Circle,
            X = 30, Y = 70, Width = 40, Height = 40
        };
        state.Nodes.Add(child);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        editor.MoveNodes(gesture.Baseline, [parent], 100, 60);
        gesture.CommitSilently();

        Assert.Equal(100, parent.X);
        Assert.Equal(160, parent.Y);
        // The child followed the parent exactly one step, not two.
        Assert.Equal(130, child.X);
        Assert.Equal(130, child.Y);
    }

    [Fact]
    public void AGroupMoveIsUndoneInASingleStep()
    {
        var (state, editor, _) = NewDesigner();
        var a = AddNode(state, 0, 100, "a");
        var b = AddNode(state, 200, 100, "b");
        state.SelectNodes([a, b], a);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        editor.MoveNodes(gesture.Baseline, state.SelectedNodes.ToList(), 75, 0);
        gesture.Commit("Move nodes");

        state.Undo();
        Assert.Equal(0, a.X);
        Assert.Equal(200, b.X);
    }

    [Fact]
    public void DeletingASelectionIsUndoableIncludingAttachedBoundaryEvents()
    {
        var (state, _, _) = NewDesigner();
        var parent = AddNode(state, 0, 100, "parent");
        var child = new DesignerNode
        {
            NodeData = new TimerBoundaryEvent { ParentNodeId = parent.NodeData.Id },
            Title = "timer",
            Shape = NodeShape.Circle,
            X = 30, Y = 70, Width = 40, Height = 40
        };
        state.Nodes.Add(child);
        state.SelectNodes([parent], parent);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        state.DeleteSelection();
        gesture.Commit("Delete selection");
        Assert.Empty(state.Nodes);

        state.Undo();
        Assert.Equal(2, state.Nodes.Count);
    }

    [Fact]
    public void ConsecutiveEditsOfOnePropertyAreASingleUndoStep()
    {
        var (state, _, _) = NewDesigner();
        var node = AddNode(state, 0, 100);
        var original = node.Title;

        foreach (char c in "Hello") state.ApplyEdits("Node name", (node, nameof(DesignerNode.Title), c.ToString()));

        Assert.Equal("o", node.Title);
        // One entry for the whole burst of keystrokes, not one per character.
        state.Undo();
        Assert.Equal(original, node.Title);
        state.Redo();
        Assert.Equal("o", node.Title);
    }

    [Fact]
    public async Task UndoingBackToTheSavedStateClearsTheDirtyFlag()
    {
        var (state, editor, store) = NewDesigner();
        var a = AddNode(state, 0, 100);
        state.Compile();
        await state.SaveDraftAsync();
        Assert.Equal(1, store.SaveCount);
        Assert.False(state.HasUnsavedChanges);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        editor.MoveNodes(gesture.Baseline, [a], 40, 40);
        gesture.Commit("Move nodes");
        Assert.True(state.HasUnsavedChanges);

        state.Undo();
        Assert.False(state.HasUnsavedChanges);
    }

    [Fact]
    public async Task AFailedSaveKeepsTheWorkAndReportsTheError()
    {
        var (state, editor, store) = NewDesigner();
        var a = AddNode(state, 0, 100);
        state.Compile();
        await state.SaveDraftAsync();
        Assert.Equal(SaveState.Saved, state.SaveState);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        editor.MoveNodes(gesture.Baseline, [a], 40, 0);
        gesture.Commit("Move nodes");

        store.FailNextSave = true;
        state.Compile();
        await state.SaveDraftAsync();

        Assert.Equal(SaveState.Failed, state.SaveState);
        Assert.NotNull(state.LastSaveError);
        Assert.True(state.HasUnsavedChanges);
        Assert.Equal(40, a.X);

        // The retry succeeds and clears the failure.
        await state.SaveDraftAsync();
        Assert.Equal(SaveState.Saved, state.SaveState);
        Assert.Null(state.LastSaveError);
    }
}
