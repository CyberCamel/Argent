using System.Security.Claims;
using Argent.Core.Authorization;
using Argent.Core.Workflows;
using Argent.Core.Workflows.BoundaryEvents;
using Argent.WebComponents.Workflows.Modeler.Editing;
using Argent.WebComponents.Workflows.Modeler.Interaction;
using Argent.WebComponents.Workflows.Modeler.Navigation;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Argent.WebComponents.Workflows.Modeler.Utils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Argent.WebComponents.Workflows.Modeler;

/// <summary>
/// Interaction half of the workflow modeler. Pointer handling, gesture lifecycle and
/// viewport logic live here; the markup in <c>WorkflowModeler.razor</c> only renders what
/// these services decide. Gestures go through <see cref="ModelerInteractionController"/>
/// so each one is a single reversible transaction, and geometry goes through
/// <see cref="GeometryEditor"/> so every gesture follows the same rules.
/// </summary>
public partial class WorkflowModeler : IAsyncDisposable
{
    [Inject] private DesignerService State { get; set; } = null!;
    [Inject] private IWorkflowNodeRegistry Registry { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IResourceOwnershipService OwnershipService { get; set; } = null!;

    [Parameter] public ClaimsPrincipal? User { get; set; }
    [Parameter] public Guid? WorkflowId { get; set; }
    [Parameter] public bool IsViewMode { get; set; }

    private bool EffectiveReadOnly => IsViewMode || State.IsReadOnlyVersion;

    private ElementReference _canvasContainer;
    private DotNetObjectReference<WorkflowModeler>? _dotNetRef;
    private ModelerSession Session { get; } = new();
    private GeometryEditor Editor { get; set; } = null!;
    private ModelerInteractionController Gesture { get; set; } = null!;
    private ModelerClipboard Clipboard { get; } = new();

    private enum ActiveTool { Select, AddSpace }
    private ActiveTool _activeTool = ActiveTool.Select;
    private Guid? _highlightedNodeId;
    private bool _showArrange;
    private bool _showShortcuts;
    private bool _temporaryPan;

    private enum Mode
    {
        Idle, Panning, TemporaryPan, Selecting, DraggingNodes, Connecting, ResizingNode,
        SegmentDrag, WaypointDrag, SpaceTool, DraggingPool, ResizingLane, ResizingPool
    }

    private Mode _mode = Mode.Idle;

    private NodeTypeDescriptor? _pendingNodeDesc;
    private double _pendingNodeX, _pendingNodeY;
    private double _pendingNodeW = 160, _pendingNodeH = 80;
    private NodeShape _pendingNodeShape;
    private string _pendingNodeCss = "";
    private string _pendingNodeTitle = "";

    private DesignerNode? _anchorNode;
    private DesignerNode? _editingNode;
    private DesignerConnection? _activeConnection;
    private double _startMouseX, _startMouseY;
    private double _startPanX, _startPanY;
    private double _segmentDragAnchorX, _segmentDragAnchorY;

    /// <summary>
    /// Thresholds are measured in screen pixels and divided by the zoom, so a gesture
    /// covers the same distance on screen at 25% and at 400%.
    /// </summary>
    private const double DragThresholdPx = 4;
    private const double SnapTolerancePx = 10;
    private const double SegmentHitTolerancePx = 18;
    private const double InsertionTolerancePx = 15;

    private ConnectionDraft? _draftConnection;
    private DesignerConnection? _rerouteOriginal;
    private int _draggedWpIdx = -1;
    private int _segmentIdx = -1;
    private bool _segmentDragPending;
    private char _activeSegmentAxis = '\0';
    private double? _segmentGuide;
    private double? _segmentLastValid;
    private bool _segmentMoved;
    private DesignerConnection? _insertionTarget;

    private double _spaceStartX, _spaceStartY;
    private double _spaceCursorX, _spaceCursorY;
    private double _laneDividerAnchorY;
    private bool _spaceIsHorizontal;
    private bool _spaceDirectionLocked;

    private double _selStartX, _selStartY;
    private double _selEndX, _selEndY;

    private DesignerPool? _activePool;
    private DesignerLane? _topLane, _botLane;
    private bool _poolResizingWidth, _poolResizingHeight;
    private bool _initialCanvasFitDone;
    private GeometryEditor.ResizeEdges _resizeEdges;

    private SnapResult? _snapResult;
    private const double SnapHighlightPadding = 4;

    private Timer? _autoSaveTimer;
    private bool _autoSaving;

    private EventCallback<ModelerPointerEventArgs> _pointerMoveCallback;

    private static readonly (AlignMode Mode, string Icon, string Label)[] ArrangeOptions =
    [
        (AlignMode.Left, "bi-align-left", "Left"),
        (AlignMode.CenterHorizontal, "bi-align-middle", "Centre horizontally"),
        (AlignMode.Right, "bi-align-right", "Right"),
        (AlignMode.Top, "bi-align-top", "Top"),
        (AlignMode.CenterVertical, "bi-align-center", "Middle"),
        (AlignMode.Bottom, "bi-align-bottom", "Bottom")
    ];

    private static readonly (string Keys, string Description)[] ShortcutHelp =
    [
        ("V", "Select tool"),
        ("S", "Add or remove space"),
        ("H / Space (hold)", "Pan temporarily"),
        ("Ctrl+Z", "Undo"),
        ("Ctrl+Y, Ctrl+Shift+Z", "Redo"),
        ("Ctrl+C, Ctrl+X", "Copy, cut selection"),
        ("Ctrl+V", "Paste"),
        ("Ctrl+D", "Duplicate"),
        ("Delete", "Delete selection"),
        ("Arrows", "Nudge by 1 unit"),
        ("Shift+arrows", "Nudge by 10 units"),
        ("Ctrl+S", "Save"),
        ("Ctrl+0", "Fit to screen"),
        ("Escape", "Cancel the current gesture")
    ];

    private string SaveStatusIcon => State.SaveState switch
    {
        SaveState.Saving => "bi-arrow-repeat",
        SaveState.Failed => "bi-exclamation-triangle-fill",
        SaveState.Unsaved => "bi-circle-fill",
        _ => "bi-check-circle-fill"
    };

    private string SaveStatusClass => State.SaveState switch
    {
        SaveState.Failed => "failed",
        SaveState.Saving => "saving",
        SaveState.Unsaved => "dirty",
        _ => "clean"
    };

    private string SaveStatusTitle => State.SaveState switch
    {
        SaveState.Saving => "Saving…",
        SaveState.Failed => $"Saving failed: {State.LastSaveError}",
        SaveState.Unsaved => "Unsaved changes",
        _ => "All changes saved"
    };

    // Movement branches notify the service when they change the scene. Suppress the
    // additional automatic render (and all idle-hover renders) for this event.
    private sealed class PointerMoveHandler(Action<ModelerPointerEventArgs> callback) : IHandleEvent
    {
        public void Invoke(ModelerPointerEventArgs e) => callback(e);
        Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? arg) => item.InvokeAsync(arg);
    }

    protected override void OnInitialized()
    {
        Editor = GeometryEditor.For(State);
        Gesture = new ModelerInteractionController(State);
        State.OnChange += StateHasChanged;
        var handler = new PointerMoveHandler(OnPointerMove);
        _pointerMoveCallback = EventCallback.Factory.Create<ModelerPointerEventArgs>(handler, handler.Invoke);
    }

    protected override async Task OnInitializedAsync()
    {
        State.UserId = User?.Identity?.Name ?? "Unknown";
        if (WorkflowId.HasValue)
            await State.LoadWorkflowAsync(WorkflowId.Value);
        _autoSaveTimer = new Timer(AutoSaveTick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("Modeler.init", _canvasContainer, _dotNetRef);
        await JS.InvokeVoidAsync("Modeler.syncClocks");
    }

    [JSInvokable]
    public void OnCanvasResized(double width, double height)
    {
        Session.CanvasWidth = width;
        Session.CanvasHeight = height;
        if (!_initialCanvasFitDone && width > 0 && height > 0)
        {
            _initialCanvasFitDone = true;
            // A newly opened model is framed once. Everything after this is an explicit action.
            if (State.Nodes.Count > 0 || State.Pools.Count > 0) FitToScreen();
            return;
        }
        State.Notify();
    }

    private void AutoSaveTick(object? state)
    {
        if (_autoSaving) return;
        _autoSaving = true;
        _ = InvokeAsync(async () =>
        {
            try
            {
                // Skipped while a gesture is open, so a half-finished drag is never saved.
                await State.TryAutoSaveAsync(EffectiveReadOnly);
                StateHasChanged();
            }
            finally
            {
                _autoSaving = false;
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _autoSaveTimer?.Dispose();
        State.OnChange -= StateHasChanged;
        if (_dotNetRef == null) return;

        try
        {
            await JS.InvokeVoidAsync("Modeler.destroy", _canvasContainer);
        }
        catch (JSDisconnectedException)
        {
            // Circuit already torn down; nothing to clean up client-side.
        }
        _dotNetRef.Dispose();
    }

    private Task<BoundingRect> GetCanvasRect() =>
        JS.InvokeAsync<BoundingRect>("Modeler.getDimensions", _canvasContainer).AsTask();

    private async Task<(double X, double Y)> ToWorldAsync(double clientX, double clientY)
    {
        var dims = await GetCanvasRect();
        return Session.ScreenToWorld(clientX, clientY, dims.Left, dims.Top);
    }

    /// <summary>Converts a screen-pixel distance into world units at the current zoom.</summary>
    private double ScreenToWorld(double pixels) => pixels / Session.Zoom;

    private void SetTool(ActiveTool tool)
    {
        if (EffectiveReadOnly) return;
        _activeTool = tool;
        _pendingNodeDesc = null;
        State.Notify();
    }

    private void OnToolboxPointerDown(PointerEventArgs e, NodeTypeDescriptor desc)
    {
        if (EffectiveReadOnly) return;
        _pendingNodeDesc = desc;
        _pendingNodeW = desc.DefaultWidth;
        _pendingNodeH = desc.DefaultHeight;
        _pendingNodeShape = desc.Shape;
        _pendingNodeCss = desc.CssClass;
        _pendingNodeTitle = desc.DisplayName;
    }

    // ======================== POINTER HANDLERS ========================

    private void StartPointer(PointerEventArgs e)
    {
        _startMouseX = e.ClientX;
        _startMouseY = e.ClientY;
    }

    private bool CrossedDragThreshold(double clientX, double clientY) =>
        Math.Abs(clientX - _startMouseX) > DragThresholdPx ||
        Math.Abs(clientY - _startMouseY) > DragThresholdPx;

    private (double X, double Y) PointerOrigin(ModelerPointerEventArgs e) =>
        Session.ScreenToWorld(_startMouseX, _startMouseY, e.CanvasLeft, e.CanvasTop);

    private async Task OnPointerDown(PointerEventArgs e)
    {
        if (_mode != Mode.Idle) return;

        StartPointer(e);

        // A read-only view can only pan; it never begins a mutation.
        if (EffectiveReadOnly)
        {
            BeginPan();
            return;
        }

        var (wx, wy) = await ToWorldAsync(e.ClientX, e.ClientY);

        if (_pendingNodeDesc != null)
        {
            PlaceNodeFromToolbox(wx, wy);
            return;
        }

        if (_activeTool == ActiveTool.AddSpace)
        {
            BeginSpaceTool(wx, wy);
            return;
        }

        // Middle-click, or space held for a temporary pan, pans instead of selecting.
        if (e.Button == 1 || _temporaryPan)
        {
            BeginPan();
            return;
        }

        // Last node in document order renders on top, so it wins the hit-test.
        var hitNode = State.Nodes.LastOrDefault(n =>
            wx >= n.X && wx <= n.X + n.Width &&
            wy >= n.Y && wy <= n.Y + n.Height);

        if (hitNode != null)
        {
            BeginNodeDrag(hitNode, e.ShiftKey || e.CtrlKey || e.MetaKey);
            return;
        }

        _editingNode = null;
        _snapResult = null;

        Gesture.Begin();
        State.DeselectAll();
        _mode = Mode.Selecting;
        _selStartX = _selEndX = wx;
        _selStartY = _selEndY = wy;
        State.Notify();
    }

    private void BeginPan()
    {
        _mode = Mode.Panning;
        _startPanX = Session.PanX;
        _startPanY = Session.PanY;
    }

    private void BeginSpaceTool(double wx, double wy)
    {
        Gesture.Begin();
        _mode = Mode.SpaceTool;
        _spaceStartX = wx;
        _spaceStartY = wy;
        _spaceDirectionLocked = false;
        State.DeselectAll();
        _editingNode = null;
        _snapResult = null;
        State.Notify();
    }

    private void BeginNodeDrag(DesignerNode node, bool additive)
    {
        // The baseline is taken before the selection changes, so Escape restores the
        // selection as well as the geometry.
        Gesture.Begin();

        if (additive)
        {
            State.ToggleNodeSelection(node);
        }
        else
        {
            if (State.SelectedNodes.Contains(node))
            {
                State.SelectedNode = node;
                State.Notify();
            }
            else
            {
                State.SelectNodes([node], node);
            }

            if (_editingNode != null && _editingNode != node)
                _editingNode = null;
        }

        _anchorNode = node;
        _mode = Mode.DraggingNodes;
        State.Notify();
    }

    private void OnPointerMove(ModelerPointerEventArgs e)
    {
        if (_mode == Mode.Idle && _pendingNodeDesc == null && _activeTool != ActiveTool.AddSpace) return;

        var (wx, wy) = Session.ScreenToWorld(e.ClientX, e.ClientY, e.CanvasLeft, e.CanvasTop);

        if (_pendingNodeDesc != null)
        {
            UpdatePendingNodePreview(wx, wy);
            return;
        }

        if (_mode == Mode.Idle && _activeTool == ActiveTool.AddSpace)
        {
            _spaceCursorX = wx;
            _spaceCursorY = wy;
            State.Notify();
        }

        switch (_mode)
        {
            case Mode.Panning:
            case Mode.TemporaryPan:
                var origin = PointerOrigin(e);
                Session.PanX = _startPanX - (wx - origin.X);
                Session.PanY = _startPanY - (wy - origin.Y);
                State.Notify();
                break;

            case Mode.Selecting:
                _selEndX = wx;
                _selEndY = wy;
                State.Notify();
                break;

            case Mode.SpaceTool:
                UpdateSpaceTool(e, wx, wy);
                break;

            case Mode.DraggingNodes:
                if (CrossedDragThreshold(e.ClientX, e.ClientY)) UpdateNodeDrag(e, wx, wy);
                break;

            case Mode.Connecting:
                UpdateConnectionDraft(wx, wy);
                break;

            case Mode.ResizingNode:
                if (!CrossedDragThreshold(e.ClientX, e.ClientY) || _anchorNode == null) break;
                var nodeOrigin = PointerOrigin(e);
                Editor.ResizeNode(_anchorNode, _resizeEdges, wx - nodeOrigin.X, wy - nodeOrigin.Y);
                Editor.RefreshRoutes(Gesture.Baseline, [_anchorNode]);
                Gesture.Changed();
                break;

            case Mode.WaypointDrag:
                if (!CrossedDragThreshold(e.ClientX, e.ClientY) || _activeConnection == null || _draggedWpIdx < 0) break;
                RoutingService.DragWaypoint(_activeConnection.Waypoints, _draggedWpIdx, wx, wy);
                Gesture.Changed();
                break;

            case Mode.SegmentDrag:
                if (_segmentDragPending)
                {
                    // A press without movement is a click on the connection, not a drag.
                    if (!CrossedDragThreshold(e.ClientX, e.ClientY)) break;
                    _segmentDragPending = false;
                }
                UpdateSegmentDrag(wx, wy);
                break;

            case Mode.DraggingPool:
                if (!CrossedDragThreshold(e.ClientX, e.ClientY) || _activePool == null) break;
                var poolOrigin = PointerOrigin(e);
                var baseline = Gesture.Baseline;
                var poolStart = baseline.PoolOriginOf(_activePool);
                Editor.MovePool(baseline, _activePool,
                    poolStart.X + (wx - poolOrigin.X),
                    poolStart.Y + (wy - poolOrigin.Y));
                Gesture.Changed();
                break;

            case Mode.ResizingLane:
                if (!CrossedDragThreshold(e.ClientX, e.ClientY) || _topLane == null || _botLane == null) break;
                var laneOrigin = PointerOrigin(e);
                Editor.ResizeLaneDivider(_topLane, _botLane,
                    _laneDividerAnchorY + (wy - laneOrigin.Y));
                Gesture.Changed();
                break;

            case Mode.ResizingPool:
                if (!CrossedDragThreshold(e.ClientX, e.ClientY) || _activePool == null) break;
                var resizeOrigin = PointerOrigin(e);
                Editor.ResizePool(_activePool, _poolResizingWidth, _poolResizingHeight,
                    wx - resizeOrigin.X, wy - resizeOrigin.Y);
                Gesture.Changed();
                break;
        }
    }

    private void UpdatePendingNodePreview(double wx, double wy)
    {
        if (typeof(BoundaryEvent).IsAssignableFrom(_pendingNodeDesc!.NodeType))
        {
            var parentHint = BoundaryGeometry.FindParentCandidate(State.Nodes, wx, wy);
            if (parentHint != null)
            {
                (_pendingNodeX, _pendingNodeY) = BoundaryGeometry.SnapToPerimeter(
                    parentHint, wx, wy, _pendingNodeW, _pendingNodeH);
                _snapResult = new SnapResult { TargetNode = parentHint };
            }
            else
            {
                _pendingNodeX = wx - _pendingNodeW / 2;
                _pendingNodeY = wy - _pendingNodeH / 2;
                _snapResult = null;
            }
        }
        else
        {
            _pendingNodeX = wx - _pendingNodeW / 2;
            _pendingNodeY = wy - _pendingNodeH / 2;
        }
        State.Notify();
    }

    private void UpdateNodeDrag(ModelerPointerEventArgs e, double wx, double wy)
    {
        if (_anchorNode == null) return;

        var baseline = Gesture.Baseline;
        var (originX, originY) = baseline.OriginOf(_anchorNode);
        double rawX = originX + (wx - PointerOrigin(e).X);
        double rawY = originY + (wy - PointerOrigin(e).Y);

        bool detached = _anchorNode.NodeData is BoundaryEvent && !State.SelectedNodes.Contains(_anchorNode);
        if (detached)
        {
            // A boundary event dragged on its own follows the pointer along the parent edge.
            var parent = FindParentCandidate(rawX + _anchorNode.Width / 2, rawY + _anchorNode.Height / 2);
            if (parent != null)
            {
                (_anchorNode.X, _anchorNode.Y) = BoundaryGeometry.SnapToPerimeter(
                    parent, rawX + _anchorNode.Width / 2, rawY + _anchorNode.Height / 2,
                    _anchorNode.Width, _anchorNode.Height);
                _snapResult = new SnapResult { TargetNode = parent };
            }
            Editor.RefreshRoutes(baseline, [_anchorNode]);
        }
        else
        {
            // Dragging any selected node moves the whole selection from the baseline.
            var moving = State.SelectedNodes.Count > 0 ? State.SelectedNodes.ToList() : [_anchorNode];
            var snap = GetSnappedPosition(_anchorNode, State.Nodes, rawX, rawY, ScreenToWorld(SnapTolerancePx));
            double targetX = snap.XSnapAxis != null ? snap.SnappedX : rawX;
            double targetY = snap.YSnapAxis != null ? snap.SnappedY : rawY;

            Editor.MoveNodes(baseline, moving, targetX - originX, targetY - originY);
            _snapResult = snap.XSnapAxis != null || snap.YSnapAxis != null ? snap : null;
        }

        // Preview the connection this node would be inserted into before the drop commits.
        _insertionTarget = ConnectionEditing.FindInsertionTarget(
            State, _anchorNode, ScreenToWorld(InsertionTolerancePx));
        Gesture.Changed();
    }

    private void UpdateSpaceTool(ModelerPointerEventArgs e, double wx, double wy)
    {
        double screenDx = e.ClientX - _startMouseX;
        double screenDy = e.ClientY - _startMouseY;

        if (!_spaceDirectionLocked &&
            (Math.Abs(screenDx) > DragThresholdPx || Math.Abs(screenDy) > DragThresholdPx))
        {
            _spaceDirectionLocked = true;
            _spaceIsHorizontal = Math.Abs(screenDx) > Math.Abs(screenDy);
        }
        if (!_spaceDirectionLocked) return;

        // The tool measures the pointer in screen pixels, so compression is the same
        // gesture at every zoom level.
        double delta = _spaceIsHorizontal ? screenDx : screenDy;
        Editor.ApplySpaceTool(Gesture.Baseline, _spaceStartX, _spaceStartY, _spaceIsHorizontal, delta);
        Gesture.Changed();
    }

    private void UpdateConnectionDraft(double wx, double wy)
    {
        if (_draftConnection == null) return;

        _draftConnection.MouseX = wx;
        _draftConnection.MouseY = wy;
        _draftConnection.TargetHint = State.Nodes.LastOrDefault(n =>
            n != _draftConnection.FixedNode &&
            wx >= n.X && wx <= n.X + n.Width &&
            wy >= n.Y && wy <= n.Y + n.Height);
        State.Notify();
    }

    private void UpdateSegmentDrag(double wx, double wy)
    {
        if (_activeConnection == null || _segmentIdx < 0) return;

        var wps = _activeConnection.Waypoints;
        bool horizontal = Math.Abs(wps[_segmentIdx].Y - wps[_segmentIdx + 1].Y) < 0.001;
        _activeSegmentAxis = horizontal ? 'H' : 'V';
        _segmentGuide = horizontal ? wps[_segmentIdx].Y : wps[_segmentIdx].X;

        // A segment moves on one axis only, and stops at the last position that still
        // produces a valid route rather than reversing the arrow or crossing a node.
        _segmentLastValid ??= horizontal ? wps[_segmentIdx].Y : wps[_segmentIdx].X;
        RoutingService.DragSegment(wps, _segmentIdx,
            ref _segmentDragAnchorX, ref _segmentDragAnchorY, wx, wy);

        if (ElasticRouter.IsRouteValid(_activeConnection, State.Nodes))
        {
            _segmentLastValid = horizontal ? wps[_segmentIdx].Y : wps[_segmentIdx].X;
            _segmentMoved = true;
            Gesture.Changed();
            return;
        }

        if (horizontal) wps[_segmentIdx].Y = wps[_segmentIdx + 1].Y = _segmentLastValid.Value;
        else wps[_segmentIdx].X = wps[_segmentIdx + 1].X = _segmentLastValid.Value;
        _segmentGuide = _segmentLastValid;
        // Re-anchor so the next pointer position is measured from here and the clamp holds.
        _segmentDragAnchorX = wx;
        _segmentDragAnchorY = wy;
        State.Notify();
    }

    private async Task OnPointerUp(PointerEventArgs e)
    {
        if (_mode == Mode.Idle && _pendingNodeDesc == null) return;

        // Releasing a toolbox drag over the canvas is the natural way to place a node,
        // and it is the only path that can work when the press started outside the canvas:
        // the canvas never received a pointerdown for that gesture, so arming happened on
        // the toolbox and there is no second click to commit it.
        if (_pendingNodeDesc != null)
        {
            var (px, py) = await ToWorldAsync(e.ClientX, e.ClientY);
            PlaceNodeFromToolbox(px, py);
            return;
        }

        await FinishActiveGestureAsync(e);
    }

    /// <summary>
    /// The pointer left the canvas. This also fires while a toolbox drag is being carried
    /// out of the canvas, so it must never place the pending node - the user can still drag
    /// back in. A pending node is deliberately left armed. Any gesture that was genuinely
    /// in progress is finished exactly as a release over the canvas would finish it.
    /// </summary>
    private async Task OnPointerLeave(PointerEventArgs e)
    {
        if (_mode == Mode.Idle) return;
        await FinishActiveGestureAsync(e);
    }

    private async Task FinishActiveGestureAsync(PointerEventArgs e)
    {
        // Apply the release position even if the browser emitted no final move.
        if (_mode is Mode.DraggingNodes or Mode.SpaceTool or Mode.DraggingPool or Mode.SegmentDrag)
        {
            var rect = await GetCanvasRect();
            OnPointerMove(new ModelerPointerEventArgs
            {
                ClientX = e.ClientX, ClientY = e.ClientY,
                CanvasLeft = rect.Left, CanvasTop = rect.Top
            });
        }

        switch (_mode)
        {
            case Mode.Selecting:
                FinishRubberBand();
                break;
            case Mode.Connecting:
                await TryFinalizeConnection(e.ClientX, e.ClientY);
                break;
            case Mode.DraggingNodes:
                CommitNodeDrag();
                break;
            case Mode.WaypointDrag:
            case Mode.SegmentDrag:
                CommitSegmentEdit();
                break;
            case Mode.SpaceTool:
                Gesture.Commit("Adjust space");
                break;
            case Mode.DraggingPool:
                Gesture.Commit("Move pool");
                break;
            case Mode.ResizingLane:
                Gesture.Commit("Resize lane");
                break;
            case Mode.ResizingPool:
                Gesture.Commit("Resize pool");
                break;
            case Mode.ResizingNode:
                Gesture.Commit("Resize node");
                break;
            case Mode.Panning:
            case Mode.TemporaryPan:
                break;
        }

        // Whatever the branch did, the gesture is over: clearing the mode is what stops a
        // later pointer move from acting on a controller whose baseline is already closed.
        ResetGestureState();
    }

    /// <summary>
    /// A cancelled pointer (the browser took the gesture away) or a lost window discards
    /// the operation instead of committing half of it.
    /// </summary>
    private void OnPointerCancel()
    {
        if (_mode == Mode.Idle) return;
        CancelGesture();
    }

    private void OnCanvasBlur() => CancelGesture();

    private void CancelGesture()
    {
        Gesture.Cancel();
        _draftConnection = null;
        _rerouteOriginal = null;
        _pendingNodeDesc = null;
        _activeTool = ActiveTool.Select;
        ResetGestureState();
        State.Notify();
    }

    private void ResetGestureState()
    {
        _mode = Mode.Idle;
        _anchorNode = null;
        _activeConnection = null;
        _activePool = null;
        _topLane = null;
        _botLane = null;
        _poolResizingWidth = false;
        _poolResizingHeight = false;
        _draggedWpIdx = -1;
        _segmentIdx = -1;
        _segmentDragPending = false;
        _activeSegmentAxis = '\0';
        _segmentGuide = null;
        _segmentLastValid = null;
        _segmentMoved = false;
        _insertionTarget = null;
        _spaceDirectionLocked = false;
        _resizeEdges = default;
        if (_snapResult == null) return;
        _snapResult = null;
        State.Notify();
    }

    private void FinishRubberBand()
    {
        double rx = Math.Min(_selStartX, _selEndX);
        double ry = Math.Min(_selStartY, _selEndY);
        double rw = Math.Abs(_selEndX - _selStartX);
        double rh = Math.Abs(_selEndY - _selStartY);
        double threshold = ScreenToWorld(DragThresholdPx);

        if (rw > threshold || rh > threshold)
        {
            var hit = State.Nodes.Where(n =>
                n.X + n.Width >= rx && n.X <= rx + rw &&
                n.Y + n.Height >= ry && n.Y <= ry + rh).ToList();
            if (hit.Count > 0) State.SelectNodes(hit, hit[^1]);
        }
        // Selection is not part of the undoable canvas state, so a marquee records nothing.
        Gesture.CommitSilently();
        State.Notify();
    }

    private void CommitNodeDrag()
    {
        if (_anchorNode == null)
        {
            Gesture.CommitSilently();
            return;
        }

        if (_insertionTarget != null)
        {
            ConnectionEditing.SplitConnection(State, _anchorNode, _insertionTarget);
            _insertionTarget = null;
        }
        Gesture.Commit("Move nodes");
    }

    private void CommitSegmentEdit()
    {
        if (_activeConnection == null || _segmentDragPending || !_segmentMoved)
        {
            // A click, or a drag that never found a valid position, records nothing.
            Gesture.CommitSilently();
            return;
        }

        // The manual edit is stored as route intent and immediately reapplied through the
        // elastic router, so what is saved is exactly what a reload reproduces.
        ElasticRouter.CommitIntent(_activeConnection, State.Nodes);
        Gesture.Commit("Edit connection route");
    }

    // ======================== POOL / LANE ========================

    private void HandleAddPool()
    {
        if (EffectiveReadOnly) return;

        Gesture.Begin();
        var cx = Session.PanX + Session.CanvasWidth / Session.Zoom / 2;
        var cy = Session.PanY + Session.CanvasHeight / Session.Zoom / 2;
        var pool = State.AddPool(cx - 420, cy - 200);
        State.Select(pool);
        Gesture.Commit("Add pool");
    }

    private async Task OnPoolHeaderPointerDown(PointerEventArgs e, DesignerPool pool)
    {
        if (_mode != Mode.Idle) return;
        StartPointer(e);

        if (_pendingNodeDesc != null)
        {
            var (wx, wy) = await ToWorldAsync(e.ClientX, e.ClientY);
            PlaceNodeFromToolbox(wx, wy);
            return;
        }

        if (EffectiveReadOnly)
        {
            State.Select(pool);
            return;
        }

        Gesture.Begin();
        State.Select(pool);
        _activePool = pool;
        _mode = Mode.DraggingPool;
        State.Notify();
    }

    private async Task OnLaneHeaderPointerDown(PointerEventArgs e, DesignerLane lane)
    {
        if (_mode != Mode.Idle) return;

        if (_pendingNodeDesc != null)
        {
            var (wx, wy) = await ToWorldAsync(e.ClientX, e.ClientY);
            PlaceNodeFromToolbox(wx, wy);
            return;
        }

        State.Select(lane);
        State.Notify();
    }

    private async Task OnPoolResizePointerDown(PointerEventArgs e, DesignerPool pool, bool resizeWidth, bool resizeHeight)
    {
        if (EffectiveReadOnly || _mode != Mode.Idle) return;
        StartPointer(e);

        if (_pendingNodeDesc != null)
        {
            var (wx, wy) = await ToWorldAsync(e.ClientX, e.ClientY);
            PlaceNodeFromToolbox(wx, wy);
            return;
        }

        Gesture.Begin();
        State.Select(pool);
        _activePool = pool;
        _poolResizingWidth = resizeWidth;
        _poolResizingHeight = resizeHeight;
        _mode = Mode.ResizingPool;
        State.Notify();
    }

    private async Task OnLaneDividerPointerDown(PointerEventArgs e, DesignerLane top, DesignerLane bottom)
    {
        if (EffectiveReadOnly || _mode != Mode.Idle) return;
        StartPointer(e);

        if (_pendingNodeDesc != null)
        {
            var (wx, wy) = await ToWorldAsync(e.ClientX, e.ClientY);
            PlaceNodeFromToolbox(wx, wy);
            return;
        }

        Gesture.Begin();
        _topLane = top;
        _botLane = bottom;
        // The divider's world position at gesture start, so every pointer position is
        // measured from the baseline instead of accumulating.
        _laneDividerAnchorY = top.Y + top.Height;
        _mode = Mode.ResizingLane;
    }

    // ======================== ANCHOR / CONNECTION ========================

    private async Task OnAnchorPointerDown(PointerEventArgs e, DesignerNode node, AnchorDirection dir)
    {
        if (EffectiveReadOnly) return;

        StartPointer(e);
        var (wx, wy) = await ToWorldAsync(e.ClientX, e.ClientY);

        Gesture.Begin();
        _mode = Mode.Connecting;
        _draftConnection = new ConnectionDraft
        {
            FixedNode = node,
            FixedDir = dir,
            MouseX = wx,
            MouseY = wy
        };
        State.Notify();
    }

    private async Task TryFinalizeConnection(double clientX, double clientY)
    {
        if (_draftConnection == null) return;

        var (wx, wy) = await ToWorldAsync(clientX, clientY);
        var fixedNode = _draftConnection.FixedNode;
        var fixedDir = _draftConnection.FixedDir;
        var original = _rerouteOriginal;

        var dropNode = State.Nodes.LastOrDefault(n =>
            n != fixedNode &&
            wx >= n.X && wx <= n.X + n.Width &&
            wy >= n.Y && wy <= n.Y + n.Height);

        if (dropNode == null)
        {
            // An empty drop is invalid: cancelling restores the original connection with
            // its label, condition, task action and route intent intact.
            CancelGesture();
            return;
        }

        DesignerConnection connection;
        if (_draftConnection.FixedEndIsTarget)
        {
            // Source endpoint re-drag: the dropped node becomes the new source and the fixed
            // node keeps its target role and anchor.
            var (_, _, sourceDir) = AnchorService.GetClosestAnchor(dropNode, (wx, wy));
            connection = ConnectionEditing.Reconnect(dropNode, sourceDir, fixedNode, fixedDir, original);
        }
        else
        {
            var (_, _, targetDir) = AnchorService.GetBestTargetAnchor(dropNode, (wx, wy), fixedDir);
            connection = ConnectionEditing.Reconnect(fixedNode, fixedDir, dropNode, targetDir, null);
        }

        Editor.RouteConnection(connection);

        if (original != null)
        {
            int index = State.Connections.IndexOf(original);
            State.Connections.Remove(original);
            State.Connections.Insert(index >= 0 ? index : State.Connections.Count, connection);
        }
        else
        {
            State.Connections.Add(connection);
        }

        State.Select(connection);
        _rerouteOriginal = null;
        _draftConnection = null;
        _mode = Mode.Idle;
        Gesture.Commit(original == null ? "Create connection" : "Reconnect connection");
        State.Notify();
    }

    private async Task OnConnectionPointerDown(PointerEventArgs e, DesignerConnection connection)
    {
        if (_pendingNodeDesc != null) return;

        StartPointer(e);
        var (wx, wy) = await ToWorldAsync(e.ClientX, e.ClientY);

        State.Select(connection);
        _editingNode = null;
        if (EffectiveReadOnly) return;

        Gesture.Begin();
        _activeConnection = connection;
        _segmentIdx = RoutingService.FindNearestSegment(
            connection.Waypoints, wx, wy, ScreenToWorld(SegmentHitTolerancePx));
        _segmentDragPending = _segmentIdx >= 0;
        if (_segmentDragPending)
        {
            _mode = Mode.SegmentDrag;
            _segmentDragAnchorX = wx;
            _segmentDragAnchorY = wy;
        }
        State.Notify();
    }

    private async Task OnWaypointPointerDown(PointerEventArgs e, DesignerConnection connection, int idx)
    {
        if (EffectiveReadOnly) return;

        StartPointer(e);
        var (wx, wy) = await ToWorldAsync(e.ClientX, e.ClientY);

        Gesture.Begin();
        _activeConnection = connection;

        if (idx != 0 && idx != connection.Waypoints.Count - 1)
        {
            _mode = Mode.WaypointDrag;
            _draggedWpIdx = idx;
            State.Notify();
            return;
        }

        // Endpoint re-drag: detach this end and continue as a connection draft. The other
        // end stays fixed and keeps its role, so re-dragging the source endpoint produces
        // (new node) → (old target), not the reverse.
        bool draggingSourceEnd = idx == 0;
        _draftConnection = new ConnectionDraft
        {
            FixedNode = draggingSourceEnd ? connection.Target : connection.Source,
            FixedDir = draggingSourceEnd ? connection.TargetDir : connection.SourceDir,
            FixedEndIsTarget = draggingSourceEnd,
            MouseX = wx,
            MouseY = wy
        };

        State.Connections.Remove(connection);
        // Restored unchanged if the drag is cancelled or ends on empty canvas.
        _rerouteOriginal = connection;
        _mode = Mode.Connecting;
        State.Notify();
    }

    private void HandleResetRouting(DesignerConnection connection)
    {
        if (EffectiveReadOnly) return;
        Gesture.Begin();
        ConnectionEditing.ResetRoute(State, connection);
        Gesture.Commit("Reset routing");
    }

    // ======================== NODE INTERACTIONS ========================

    private void OnNodeDoubleClick(DesignerNode node)
    {
        if (EffectiveReadOnly) return;
        State.SelectNodes([node], node);
        _editingNode = node;
        State.Notify();
    }

    private void ClosePropertiesPanel()
    {
        _editingNode = null;
        State.Notify();
    }

    private void CloseConnectionEditor()
    {
        State.Select(null);
        State.Notify();
    }

    private void OnSidebarHoverNode(NodeBase? node)
    {
        _highlightedNodeId = node?.Id;
        State.Notify();
    }

    private void OnVersionLoaded()
    {
        _editingNode = null;
        _highlightedNodeId = null;
        State.Notify();
    }

    /// <summary>Selects the node behind a validation entry and frames it.</summary>
    private void FocusNode(NodeBase node)
    {
        var designerNode = State.Nodes.FirstOrDefault(n => n.NodeData.Id == node.Id);
        if (designerNode == null) return;

        State.SelectNodes([designerNode], designerNode);
        _editingNode = designerNode;
        CanvasNavigation.Focus(Session, new GeometryEditor.Bounds(
            designerNode.X, designerNode.Y, designerNode.Width, designerNode.Height));
        State.Notify();
    }

    private void FocusSelection()
    {
        if (SelectionBounds() is not { } bounds) return;
        CanvasNavigation.Focus(Session, bounds);
        State.Notify();
    }

    private GeometryEditor.Bounds? SelectionBounds()
    {
        if (State.SelectedNodes.Count == 0) return null;
        var nodes = State.SelectedNodes;
        double minX = nodes.Min(n => n.X), minY = nodes.Min(n => n.Y);
        double maxX = nodes.Max(n => n.X + n.Width), maxY = nodes.Max(n => n.Y + n.Height);
        return new GeometryEditor.Bounds(minX, minY, maxX - minX, maxY - minY);
    }

    private void ZoomBy(double factor)
    {
        double cx = Session.CanvasWidth / 2;
        double cy = Session.CanvasHeight / 2;
        CanvasNavigation.ZoomToCursor(Session, cx, cy, 0, 0,
            factor > 1 ? -120 : 120);
        State.Notify();
    }

    /// <summary>Frames every node, pool, lane and connector, not just the node rectangles.</summary>
    private void FitToScreen()
    {
        var bounds = new List<GeometryEditor.Bounds>();
        bounds.AddRange(State.Pools.Select(p => new GeometryEditor.Bounds(p.X, p.Y, p.Width, p.Height)));
        bounds.AddRange(State.Lanes.Select(l => new GeometryEditor.Bounds(l.X, l.Y, l.Width, l.Height)));
        bounds.AddRange(State.Nodes.Select(n => new GeometryEditor.Bounds(n.X, n.Y, n.Width, n.Height)));
        bounds.AddRange(ConnectionBounds());

        if (!CanvasNavigation.FitToContent(Session, bounds)) return;
        State.Notify();
    }

    /// <summary>
    /// Connector geometry is part of the framed content, so a diagram whose routes swing
    /// far outside the nodes is still fully visible.
    /// </summary>
    private IEnumerable<GeometryEditor.Bounds> ConnectionBounds()
    {
        foreach (var connection in State.Connections)
        {
            if (connection.Waypoints.Count == 0) continue;
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var wp in connection.Waypoints)
            {
                minX = Math.Min(minX, wp.X);
                minY = Math.Min(minY, wp.Y);
                maxX = Math.Max(maxX, wp.X);
                maxY = Math.Max(maxY, wp.Y);
            }
            yield return new GeometryEditor.Bounds(minX, minY, maxX - minX, maxY - minY);
        }
    }

    // ======================== KEYBOARD ========================

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        // Bound to the whole layout so a shortcut still works after the user clicks a
        // sidebar tab, but never while the user is typing in a field.
        if (await IsTypingInAField()) return;
        bool ctrl = e.CtrlKey || e.MetaKey;
        string key = e.Key;

        if (key == "Escape")
        {
            OnEscape();
            return;
        }

        if (ctrl && key is "z" or "Z")
        {
            if (e.ShiftKey) Redo(); else Undo();
            return;
        }

        if (ctrl && key is "y" or "Y")
        {
            Redo();
            return;
        }

        if (ctrl && key is "s" or "S")
        {
            _ = HandleSave();
            return;
        }

        if (ctrl && key is "c" or "C")
        {
            CopySelection();
            return;
        }

        if (ctrl && key is "x" or "X")
        {
            CopySelection();
            DeleteSelection();
            return;
        }

        if (ctrl && key is "v" or "V")
        {
            PasteClipboard();
            return;
        }

        if (ctrl && key is "d" or "D")
        {
            CopySelection();
            PasteClipboard();
            return;
        }

        if (ctrl && key is "a" or "A")
        {
            State.SelectNodes(State.Nodes);
            return;
        }

        if (ctrl && key is "0")
        {
            FitToScreen();
            return;
        }

        if (EffectiveReadOnly) return;

        if (key is "Delete" or "Backspace")
        {
            DeleteSelection();
            return;
        }

        if (key is "v" or "V")
        {
            SetTool(ActiveTool.Select);
            return;
        }

        if (key is "s" or "S")
        {
            SetTool(ActiveTool.AddSpace);
            return;
        }

        if (key is "h" or "H" || key == " ")
        {
            _temporaryPan = key != " ";
            if (_temporaryPan) StartTemporaryPan();
            return;
        }

        if (key is "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown")
        {
            NudgeSelection(key, e.ShiftKey ? 10 : 1);
        }
    }

    private async Task OnKeyUp(KeyboardEventArgs e)
    {
        if (await IsTypingInAField()) return;
        if (e.Key is "h" or "H" || e.Key == " ")
        {
            _temporaryPan = false;
            if (_mode == Mode.TemporaryPan) _mode = Mode.Idle;
        }
    }

    /// <summary>
    /// True when the focused element is a field the user is typing into, so a modeler
    /// shortcut never fires instead of a character.
    /// </summary>
    private async Task<bool> IsTypingInAField()
    {
        try
        {
            var tag = await JS.InvokeAsync<string>("Modeler.activeTagName");
            return tag is "INPUT" or "TEXTAREA" or "SELECT";
        }
        catch (JSDisconnectedException)
        {
            return true;
        }
    }

    private void StartTemporaryPan()
    {
        _mode = Mode.TemporaryPan;
        _startPanX = Session.PanX;
        _startPanY = Session.PanY;
    }

    private void OnEscape()
    {
        if (_mode != Mode.Idle || Gesture.IsOpen || _pendingNodeDesc != null)
        {
            CancelGesture();
            return;
        }

        _pendingNodeDesc = null;
        _editingNode = null;
        State.DeselectAll();
        State.Notify();
    }

    private void Undo()
    {
        if (EffectiveReadOnly) return;
        State.Undo();
    }

    private void Redo()
    {
        if (EffectiveReadOnly) return;
        State.Redo();
    }

    private void NudgeSelection(string key, double step)
    {
        if (State.SelectedNodes.Count == 0) return;

        Gesture.Begin();
        var nodes = State.SelectedNodes.ToList();
        var baseline = Gesture.Baseline;
        double dx = key switch { "ArrowLeft" => -step, "ArrowRight" => step, _ => 0 };
        double dy = key switch { "ArrowUp" => -step, "ArrowDown" => step, _ => 0 };

        Editor.MoveNodes(baseline, nodes, dx, dy);
        Gesture.Commit("Nudge");
    }

    private void CopySelection()
    {
        if (State.SelectedNodes.Count == 0) return;
        Clipboard.Copy(State, State.SelectedNodes);
    }

    private void DeleteSelection()
    {
        if (EffectiveReadOnly) return;
        if (State.SelectedNodes.Count == 0 && State.SelectedConnection == null &&
            State.SelectedPool == null && State.SelectedLane == null) return;

        Gesture.Begin();
        State.DeleteSelection();
        Gesture.Commit("Delete selection");
    }

    private void PasteClipboard()
    {
        if (EffectiveReadOnly || !Clipboard.HasContent) return;

        Gesture.Begin();
        var (nodes, _) = Clipboard.Paste(State);
        if (nodes.Count > 0) State.SelectNodes(nodes, nodes[^1]);
        Gesture.Commit("Paste");
    }

    private void ApplyAlign(AlignMode mode)
    {
        if (EffectiveReadOnly) return;
        Gesture.Begin();
        AlignmentService.Align(State, Gesture.Baseline, mode);
        Gesture.Commit("Align selection");
        _showArrange = false;
    }

    private void ApplyDistribute(DistributeMode mode)
    {
        if (EffectiveReadOnly) return;
        Gesture.Begin();
        AlignmentService.Distribute(State, Gesture.Baseline, mode);
        Gesture.Commit("Distribute selection");
        _showArrange = false;
    }

    // ======================== PLACEMENT ========================

    private void PlaceNodeFromToolbox(double wx, double wy)
    {
        if (_pendingNodeDesc == null) return;

        var instance = Activator.CreateInstance(_pendingNodeDesc.NodeType) as NodeBase;
        if (instance == null) return;

        double nodeX = wx - _pendingNodeW / 2;
        double nodeY = wy - _pendingNodeH / 2;

        if (instance is BoundaryEvent boundaryEvent)
        {
            var parent = FindParentCandidate(wx, wy);
            if (parent == null)
            {
                // No parent under the cursor: cancel placement rather than orphaning it.
                _pendingNodeDesc = null;
                _snapResult = null;
                State.Notify();
                return;
            }
            (nodeX, nodeY) = BoundaryGeometry.SnapToPerimeter(parent, wx, wy, _pendingNodeW, _pendingNodeH);
            boundaryEvent.ParentNodeId = parent.NodeData.Id;
        }
        else if (Editor.PoolAt(wx, wy) is { } pool)
        {
            (nodeX, nodeY) = Editor.ClampToPoolContent(pool, nodeX, nodeY, _pendingNodeW, _pendingNodeH);
        }

        Gesture.Begin();
        var node = new DesignerNode
        {
            NodeData = instance,
            Title = _pendingNodeDesc.DisplayName,
            Description = _pendingNodeDesc.Description,
            Icon = _pendingNodeDesc.Icon,
            CssClass = _pendingNodeDesc.CssClass,
            Shape = _pendingNodeDesc.Shape,
            Width = _pendingNodeW,
            Height = _pendingNodeH,
            X = nodeX,
            Y = nodeY
        };

        State.Nodes.Add(node);

        // A dropped node that lands on a connector is inserted into it. A boundary event
        // belongs to its parent instead, so it never splits a connection.
        if (instance is not BoundaryEvent &&
            ConnectionEditing.FindInsertionTarget(State, node, ScreenToWorld(InsertionTolerancePx))
                is { } insertionTarget)
        {
            ConnectionEditing.SplitConnection(State, node, insertionTarget);
        }

        State.SelectNodes([node], node);
        _pendingNodeDesc = null;
        _snapResult = null;
        Gesture.Commit("Add node");
    }

    private void OnResizeStart(PointerEventArgs e, DesignerNode node, GeometryEditor.ResizeEdges edges)
    {
        if (EffectiveReadOnly) return;

        StartPointer(e);
        Gesture.Begin();
        _mode = Mode.ResizingNode;
        _anchorNode = node;
        _resizeEdges = edges;
    }

    private async Task OnMouseWheel(WheelEventArgs e)
    {
        var rect = await GetCanvasRect();
        CanvasNavigation.ZoomToCursor(Session, e.ClientX, e.ClientY, rect.Left, rect.Top, e.DeltaY);
        State.Notify();
    }

    private static string GetConnectionLabel(DesignerConnection connection)
    {
        var engine = connection.EngineConnection;
        if (!string.IsNullOrEmpty(engine.Label)) return engine.Label;
        if (engine.IsDefault) return "default";
        if (engine.Condition != null) return "[condition]";
        if (!string.IsNullOrEmpty(engine.Expression))
            return engine.Expression.Length > 18 ? engine.Expression[..18] + "…" : engine.Expression;
        return "";
    }

    /// <summary>
    /// Places a connection label on the longest segment of the path and away from the bend
    /// radius, so a rerouted connection does not end up with its label on top of a corner or
    /// an arrowhead.
    /// </summary>
    private static (double X, double Y) GetLabelAnchor(List<DesignerWaypoint> waypoints)
    {
        int bestIndex = 0;
        double bestLength = -1;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            double length = Math.Abs(waypoints[i + 1].X - waypoints[i].X) + Math.Abs(waypoints[i + 1].Y - waypoints[i].Y);
            if (length <= bestLength) continue;
            bestLength = length;
            bestIndex = i;
        }

        var a = waypoints[bestIndex];
        var b = waypoints[bestIndex + 1];
        return ((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    }

    private static double LabelHalfWidth(string label) => Math.Max(14, label.Length * 3.6);

    private DesignerNode? FindParentCandidate(double wx, double wy, DesignerNode? exclude = null) =>
        BoundaryGeometry.FindParentCandidate(State.Nodes, wx, wy, exclude);

    // ======================== SAVE / COMPILE ========================

    private async Task HandleSave()
    {
        var wasNew = !State.CurrentWorkflowId.HasValue;
        State.Compile();
        await State.SaveDraftAsync();
        State.Notify();

        if (wasNew && State.CurrentWorkflowId.HasValue)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
                await OwnershipService.GrantOwnershipAsync("Workflow", State.CurrentWorkflowId.Value, userId);

            Nav.NavigateTo($"/Workflows/Edit/{State.CurrentWorkflowId}", forceLoad: true);
        }
    }

    private void HandleCompile()
    {
        State.Compile();
        State.Notify();
    }

    private void HandleTestRun()
    {
        State.Compile();
        State.Notify();
    }

    // ======================== SNAPPING ========================

    /// <summary>
    /// Aligns a dragged node with its neighbours. The tolerance is supplied in world units
    /// by the caller so the snap feels identical at every zoom level.
    /// </summary>
    public static SnapResult GetSnappedPosition(
        DesignerNode draggingNode,
        List<DesignerNode> allNodes,
        double rawX,
        double rawY,
        double threshold = 10)
    {
        var result = new SnapResult { SnappedX = rawX, SnappedY = rawY };

        foreach (var otherNode in allNodes.Where(n => n != draggingNode))
        {
            if (Math.Abs((rawX + draggingNode.Width / 2) - (otherNode.X + otherNode.Width / 2)) < threshold)
            {
                result.SnappedX = (otherNode.X + otherNode.Width / 2) - draggingNode.Width / 2;
                result.XSnapAxis = SnapAxis.Center;
                result.TargetNode = otherNode;
            }
            else if (Math.Abs(rawX - otherNode.X) < threshold)
            {
                result.SnappedX = otherNode.X;
                result.XSnapAxis = SnapAxis.Start;
                result.TargetNode = otherNode;
            }
            else if (Math.Abs((rawX + draggingNode.Width) - (otherNode.X + otherNode.Width)) < threshold)
            {
                result.SnappedX = otherNode.X + otherNode.Width - draggingNode.Width;
                result.XSnapAxis = SnapAxis.End;
                result.TargetNode = otherNode;
            }

            if (Math.Abs((rawY + draggingNode.Height / 2) - (otherNode.Y + otherNode.Height / 2)) < threshold)
            {
                result.SnappedY = (otherNode.Y + otherNode.Height / 2) - draggingNode.Height / 2;
                result.YSnapAxis = SnapAxis.Center;
                result.TargetNode = otherNode;
            }
            else if (Math.Abs(rawY - otherNode.Y) < threshold)
            {
                result.SnappedY = otherNode.Y;
                result.YSnapAxis = SnapAxis.Start;
                result.TargetNode = otherNode;
            }
            else if (Math.Abs((rawY + draggingNode.Height) - (otherNode.Y + otherNode.Height)) < threshold)
            {
                result.SnappedY = otherNode.Y + otherNode.Height - draggingNode.Height;
                result.YSnapAxis = SnapAxis.End;
                result.TargetNode = otherNode;
            }
        }

        return result;
    }

    public sealed class BoundingRect
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
