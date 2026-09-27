using System.Text.Json.Serialization;

namespace Argent.Core.Workflows.Modeler;

/// <summary>Which coordinate pins a manually positioned route line.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RouteAxis>))]
public enum RouteAxis
{
    /// <summary>A vertical line of the path, pinned by its <c>X</c>.</summary>
    Vertical,

    /// <summary>A horizontal line of the path, pinned by its <c>Y</c>.</summary>
    Horizontal
}

/// <summary>
/// One line of a connection path that the user positioned by hand. A vertical segment
/// is described by its <see cref="X"/> and a horizontal segment by its <see cref="Y"/>;
/// the other coordinate stays null so the segment keeps its meaning when the rest of the
/// path stretches.
/// </summary>
public class RouteSegment
{
    public RouteAxis Axis { get; set; } = RouteAxis.Vertical;

    /// <summary>Position of a vertical line, or null when the segment is not vertical.</summary>
    public double? X { get; set; }

    /// <summary>Position of a horizontal line, or null when the segment is not horizontal.</summary>
    public double? Y { get; set; }

    /// <summary>
    /// Ordinal among the stored segments of the same axis. It keeps a constraint attached
    /// to the same line when the automatic route inserts or removes bends of the other
    /// axis. It is only ever applied while <see cref="ConnectionRoute.Structure"/> still
    /// matches, so the ordinal cannot drift onto an unrelated segment.
    /// </summary>
    public int Index { get; set; }

    /// <summary>The pinned coordinate: x for a vertical line, y for a horizontal one.</summary>
    [JsonIgnore]
    public double Value => Axis == RouteAxis.Vertical ? X ?? 0 : Y ?? 0;

    public RouteSegment Clone() => new() { Axis = Axis, X = X, Y = Y, Index = Index };
}

/// <summary>
/// The user's layout intent for a connection: which port sides they preferred and which
/// lines of the path they positioned. Calculated waypoints and router search caches are
/// deliberately not part of this; they are derived from the intent on every render.
/// A null route means "route automatically".
/// </summary>
public class ConnectionRoute
{
    /// <summary>Version of the persisted representation. Readers ignore unknown versions.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Port side the user chose at the source, or <see cref="AnchorDirection.None"/>.</summary>
    public AnchorDirection SourceSide { get; set; } = AnchorDirection.None;

    /// <summary>Port side the user chose at the target, or <see cref="AnchorDirection.None"/>.</summary>
    public AnchorDirection TargetSide { get; set; } = AnchorDirection.None;

    /// <summary>
    /// Direction structure of the edited path, one character per segment: <c>H</c> or
    /// <c>V</c>. The elastic router rebuilds the path from this structure plus
    /// <see cref="Segments"/>; when the structure no longer describes a buildable route the
    /// intent is suspended and an automatic route is shown instead.
    /// </summary>
    public string Structure { get; set; } = string.Empty;

    public List<RouteSegment> Segments { get; set; } = [];

    [JsonIgnore]
    public bool HasManualSegments => Segments.Count > 0;

    public ConnectionRoute Clone() => new()
    {
        Version = Version,
        SourceSide = SourceSide,
        TargetSide = TargetSide,
        Structure = Structure,
        Segments = [.. Segments.Select(s => s.Clone())]
    };
}
