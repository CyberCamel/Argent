using System.Text.Json.Serialization;

namespace Argent.Models.Workflows.Shared;

[JsonDerivedType(typeof(RelativeTimerDefinition), "relative")]
[JsonDerivedType(typeof(CronTimerDefinition), "cron")]
public abstract class TimerDefinition { }

public class RelativeTimerDefinition : TimerDefinition
{
    public int Amount { get; set; } = 1;
    public TimerUnit Unit { get; set; } = TimerUnit.Days;

    // When false, anchor is token arrival time.
    // When true, anchor is a date field from the instance domain object record.
    public bool UseField { get; set; }
    public string? FieldKey { get; set; }
}

// Uses a 6-field cron expression (seconds included: "ss mm hh dd MM dow").
// Resolved as the next occurrence after the anchor time (token arrival or field date).
public class CronTimerDefinition : TimerDefinition
{
    public string Expression { get; set; } = "0 0 9 * * *"; // daily at 09:00:00
    public string TimeZoneId { get; set; } = "UTC";
}
