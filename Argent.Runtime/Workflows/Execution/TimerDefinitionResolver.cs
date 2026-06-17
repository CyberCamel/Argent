using Argent.Models.Workflows;
using Argent.Models.Workflows.Shared;
using Cronos;

namespace Argent.Runtime.Workflows.Execution;

public static class TimerDefinitionResolver
{
    // Resolves a TimerDefinition to the concrete DateTime when the timer should fire.
    // anchor: token arrival time (or field date when definition.UseField = true, already resolved by caller).
    public static DateTime Resolve(TimerDefinition definition, DateTime anchor)
    {
        return definition switch
        {
            RelativeTimerDefinition rel => ResolveRelative(rel, anchor),
            CronTimerDefinition cron    => ResolveCron(cron, anchor),
            _ => throw new NotSupportedException($"Unknown TimerDefinition type: {definition.GetType().Name}")
        };
    }

    private static DateTime ResolveRelative(RelativeTimerDefinition def, DateTime anchor)
    {
        return def.Unit switch
        {
            TimerUnit.Seconds  => anchor.AddSeconds(def.Amount),
            TimerUnit.Minutes  => anchor.AddMinutes(def.Amount),
            TimerUnit.Hours    => anchor.AddHours(def.Amount),
            TimerUnit.Days     => anchor.AddDays(def.Amount),
            TimerUnit.Weekdays => AddWeekdays(anchor, def.Amount),
            TimerUnit.Weeks    => anchor.AddDays(def.Amount * 7),
            TimerUnit.Months   => anchor.AddMonths(def.Amount),
            TimerUnit.Years    => anchor.AddYears(def.Amount),
            _ => throw new NotSupportedException($"Unknown TimerUnit: {def.Unit}")
        };
    }

    private static DateTime ResolveCron(CronTimerDefinition def, DateTime anchor)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(def.TimeZoneId);
        var expression = CronExpression.Parse(def.Expression, CronFormat.IncludeSeconds);

        // Cronos works in UTC. Convert anchor to UTC, find next occurrence, return UTC.
        var anchorUtc = TimeZoneInfo.ConvertTimeToUtc(anchor.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(anchor, DateTimeKind.Utc)
            : anchor);

        var next = expression.GetNextOccurrence(anchorUtc, tz);
        return next
            ?? throw new InvalidOperationException($"Cron expression '{def.Expression}' has no future occurrence after {anchor:O}");
    }

    // Adds n Mon–Fri days, skipping Sat/Sun.
    private static DateTime AddWeekdays(DateTime from, int days)
    {
        var result = from;
        var remaining = days;
        while (remaining > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                remaining--;
        }
        return result;
    }
}
