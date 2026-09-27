using Argent.Core.Workflows;
using Argent.Core.Workflows.Modeler;

namespace Argent.WebComponents.Workflows.Modeler.Interaction;

/// <summary>
/// The complete state a gesture can change: which objects exist, where they are, how each
/// connection is routed, and what is selected. Captured once when a gesture starts and
/// reused for every intermediate pointer position, so a drag is computed from the gesture
/// baseline rather than from accumulated deltas.
/// </summary>
public sealed class DesignerSnapshot
{
    private readonly List<ItemGeometry> _nodes = [];
    private readonly List<ItemGeometry> _pools = [];
    private readonly List<ItemGeometry> _lanes = [];
    private readonly List<ConnectionGeometry> _connections = [];
    private readonly Dictionary<DesignerNode, (double X, double Y)> _nodeOrigins = [];
    private readonly Dictionary<DesignerPool, (double X, double Y, double Width, double Height)> _poolOrigins = [];
    private readonly Dictionary<DesignerLane, (double X, double Y, double Width, double Height)> _laneOrigins = [];
    private List<DesignerNode> _selectedNodes = [];
    private DesignerNode? _activeNode;
    private DesignerConnection? _selectedConnection;
    private DesignerPool? _selectedPool;
    private DesignerLane? _selectedLane;

    /// <summary>The position a node had when the gesture started.</summary>
    public (double X, double Y) OriginOf(DesignerNode node) =>
        _nodeOrigins.TryGetValue(node, out var origin) ? origin : (node.X, node.Y);

    public double DeltaX(DesignerNode node) => node.X - OriginOf(node).X;
    public double DeltaY(DesignerNode node) => node.Y - OriginOf(node).Y;

    public (double X, double Y, double Width, double Height) PoolOriginOf(DesignerPool pool) =>
        _poolOrigins.TryGetValue(pool, out var origin)
            ? origin
            : (pool.X, pool.Y, pool.Width, pool.Height);

    public (double X, double Y, double Width, double Height) LaneOriginOf(DesignerLane lane) =>
        _laneOrigins.TryGetValue(lane, out var origin)
            ? origin
            : (lane.X, lane.Y, lane.Width, lane.Height);

    public static DesignerSnapshot Capture(DesignerService state)
    {
        var snapshot = new DesignerSnapshot();

        for (int i = 0; i < state.Nodes.Count; i++)
        {
            var node = state.Nodes[i];
            snapshot._nodes.Add(new ItemGeometry(i, node, node.X, node.Y, node.Width, node.Height));
            snapshot._nodeOrigins[node] = (node.X, node.Y);
        }
        for (int i = 0; i < state.Pools.Count; i++)
        {
            var pool = state.Pools[i];
            snapshot._pools.Add(new ItemGeometry(i, pool, pool.X, pool.Y, pool.Width, pool.Height));
            snapshot._poolOrigins[pool] = (pool.X, pool.Y, pool.Width, pool.Height);
        }
        for (int i = 0; i < state.Lanes.Count; i++)
        {
            var lane = state.Lanes[i];
            snapshot._lanes.Add(new ItemGeometry(i, lane, lane.X, lane.Y, lane.Width, lane.Height));
            snapshot._laneOrigins[lane] = (lane.X, lane.Y, lane.Width, lane.Height);
        }
        for (int i = 0; i < state.Connections.Count; i++)
        {
            var connection = state.Connections[i];
            snapshot._connections.Add(new ConnectionGeometry(
                i, connection, connection.SourceDir, connection.TargetDir,
                connection.CopyWaypoints(), connection.Route?.Clone()));
        }

        snapshot._selectedNodes = [.. state.SelectedNodes];
        snapshot._activeNode = state.SelectedNode;
        snapshot._selectedConnection = state.SelectedConnection;
        snapshot._selectedPool = state.SelectedPool;
        snapshot._selectedLane = state.SelectedLane;
        return snapshot;
    }

    /// <summary>Puts the designer back exactly as it was, including selection.</summary>
    public void Restore(DesignerService state)
    {
        foreach (var item in _nodes) item.Apply();
        state.Nodes.Clear();
        foreach (var item in _nodes.OrderBy(n => n.Index)) state.Nodes.Add((DesignerNode)item.Item!);

        foreach (var item in _pools) item.Apply();
        state.Pools.Clear();
        foreach (var item in _pools.OrderBy(p => p.Index)) state.Pools.Add((DesignerPool)item.Item!);

        foreach (var item in _lanes) item.Apply();
        state.Lanes.Clear();
        foreach (var item in _lanes.OrderBy(l => l.Index)) state.Lanes.Add((DesignerLane)item.Item!);

        foreach (var connection in _connections) connection.Apply();
        state.Connections.Clear();
        foreach (var connection in _connections.OrderBy(c => c.Index)) state.Connections.Add(connection.Connection);

        // Pool and lane instances are reused, so their cross-references stay valid.
        RestoreSelection(state);
        state.Notify();
    }

    private void RestoreSelection(DesignerService state)
    {
        state.ClearSelectionFlags();
        state.SelectedNodes.Clear();
        foreach (var node in _selectedNodes)
        {
            if (!state.Nodes.Contains(node)) continue;
            state.SelectedNodes.Add(node);
            node.IsSelected = true;
        }
        state.SelectedNode = _activeNode != null && state.SelectedNodes.Contains(_activeNode)
            ? _activeNode
            : _selectedNodes.LastOrDefault();
        state.SelectedConnection = _selectedConnection != null && state.Connections.Contains(_selectedConnection)
            ? _selectedConnection
            : null;
        state.SelectedPool = _selectedPool != null && state.Pools.Contains(_selectedPool) ? _selectedPool : null;
        state.SelectedLane = _selectedLane != null && state.Lanes.Contains(_selectedLane) ? _selectedLane : null;
        if (state.SelectedPool != null) state.SelectedPool.IsSelected = true;
        if (state.SelectedLane != null) state.SelectedLane.IsSelected = true;
    }

    /// <summary>
    /// True when the canvas itself differs, ignoring selection. A click that only changes
    /// the selection must not create an undo entry or mark the draft dirty.
    /// </summary>
    public bool DiffersFrom(DesignerSnapshot other)
    {
        if (other == null) return true;
        if (_nodes.Count != other._nodes.Count ||
            _pools.Count != other._pools.Count ||
            _lanes.Count != other._lanes.Count ||
            _connections.Count != other._connections.Count)
            return true;

        for (int i = 0; i < _nodes.Count; i++)
            if (!_nodes[i].Matches(other._nodes[i])) return true;
        for (int i = 0; i < _pools.Count; i++)
            if (!_pools[i].Matches(other._pools[i])) return true;
        for (int i = 0; i < _lanes.Count; i++)
            if (!_lanes[i].Matches(other._lanes[i])) return true;
        for (int i = 0; i < _connections.Count; i++)
            if (!_connections[i].Matches(other._connections[i])) return true;
        return false;
    }

    private sealed class ItemGeometry
    {
        private readonly double _x, _y, _width, _height;

        public ItemGeometry(int index, object item, double x, double y, double width, double height)
        {
            Index = index;
            Item = item;
            _x = x;
            _y = y;
            _width = width;
            _height = height;
        }

        public int Index { get; }
        public object Item { get; }

        public void Apply()
        {
            switch (Item)
            {
                case DesignerNode node:
                    node.X = _x; node.Y = _y; node.Width = _width; node.Height = _height;
                    break;
                case DesignerPool pool:
                    pool.X = _x; pool.Y = _y; pool.Width = _width; pool.Height = _height;
                    break;
                case DesignerLane lane:
                    lane.X = _x; lane.Y = _y; lane.Width = _width; lane.Height = _height;
                    break;
            }
        }

        public bool Matches(ItemGeometry other) =>
            ReferenceEquals(Item, other.Item) &&
            _x.Equals(other._x) && _y.Equals(other._y) &&
            _width.Equals(other._width) && _height.Equals(other._height);
    }

    private sealed class ConnectionGeometry
    {
        private readonly AnchorDirection _sourceDir, _targetDir;
        private readonly DesignerWaypoint[] _waypoints;
        private readonly ConnectionRoute? _route;

        public ConnectionGeometry(
            int index, DesignerConnection connection, AnchorDirection sourceDir, AnchorDirection targetDir,
            List<DesignerWaypoint> waypoints, ConnectionRoute? route)
        {
            Index = index;
            Connection = connection;
            _sourceDir = sourceDir;
            _targetDir = targetDir;
            _waypoints = [.. waypoints];
            _route = route;
        }

        public int Index { get; }
        public DesignerConnection Connection { get; }

        public void Apply()
        {
            Connection.SourceDir = _sourceDir;
            Connection.TargetDir = _targetDir;
            Connection.Waypoints = [.. _waypoints.Select(w => new DesignerWaypoint { X = w.X, Y = w.Y })];
            Connection.Route = _route?.Clone();
        }

        public bool Matches(ConnectionGeometry other)
        {
            if (!ReferenceEquals(Connection, other.Connection)) return false;
            if (_sourceDir != other._sourceDir || _targetDir != other._targetDir) return false;
            if (!RouteEquals(_route, other._route)) return false;
            if (_waypoints.Length != other._waypoints.Length) return false;
            for (int i = 0; i < _waypoints.Length; i++)
            {
                if (!_waypoints[i].X.Equals(other._waypoints[i].X) ||
                    !_waypoints[i].Y.Equals(other._waypoints[i].Y)) return false;
            }
            return true;
        }

        private static bool RouteEquals(ConnectionRoute? a, ConnectionRoute? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Version != b.Version || a.SourceSide != b.SourceSide || a.TargetSide != b.TargetSide) return false;
            if (a.Structure != b.Structure || a.Segments.Count != b.Segments.Count) return false;
            for (int i = 0; i < a.Segments.Count; i++)
            {
                if (a.Segments[i].Axis != b.Segments[i].Axis ||
                    a.Segments[i].Index != b.Segments[i].Index ||
                    !a.Segments[i].Value.Equals(b.Segments[i].Value)) return false;
            }
            return true;
        }
    }
}
