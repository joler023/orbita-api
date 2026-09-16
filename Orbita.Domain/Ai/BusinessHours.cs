namespace Orbita.Domain.Ai;

/// <summary>
/// When an assistant is on duty (ORB-C08), stored on <c>ai_agents.business_hours</c> as
/// orbita-schema.dbml specifies. Null on the agent means always on — the behaviour every
/// assistant had before routing existed, so nothing changes for a tenant that never opens
/// this screen.
///
/// Evaluated in the tenant's own time zone (<c>tenants.timezone</c>): "abrimos de 9 a 6"
/// is a local statement, and converting it through UTC by hand is how a bakery in Bogotá
/// ends up closed at lunch.
/// </summary>
public sealed record BusinessHours(IReadOnlyList<BusinessHoursSlot> Slots, OutsideHoursBehavior OutsideHours)
{
    public const int MaxSlots = 21;

    /// <exception cref="ArgumentException">No slots at all, too many, or a slot that closes before it opens.</exception>
    public static BusinessHours Create(IReadOnlyList<BusinessHoursSlot> slots, OutsideHoursBehavior outsideHours)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (slots.Count > MaxSlots)
        {
            throw new ArgumentException($"Un horario admite hasta {MaxSlots} franjas.", nameof(slots));
        }

        // An empty schedule is never what anyone meant. With LeaveForTeam it silences the
        // assistant permanently — the failure nobody can diagnose from outside, arrived at
        // by saving an empty form. With AssistantAnswers it is just a slower way of writing
        // null. Turning the assistant off is what PATCH .../enabled is for.
        if (slots.Count == 0)
        {
            throw new ArgumentException(
                "Un horario sin franjas dejaría al asistente sin atender nunca. Para apagarlo, usa el interruptor del asistente.",
                nameof(slots));
        }

        foreach (var slot in slots)
        {
            // Overnight shifts are written as two slots (22:00–24:00 and 00:00–06:00).
            // Allowing closes < opens would make "is it open" ambiguous across midnight.
            if (slot.Closes <= slot.Opens)
            {
                throw new ArgumentException("Cada franja tiene que cerrar después de abrir. Para un turno que pasa la medianoche, usa dos franjas.", nameof(slots));
            }
        }

        return new BusinessHours([.. slots], outsideHours);
    }

    public bool IsOpenAt(DateTimeOffset utcNow, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var time = TimeOnly.FromDateTime(local.DateTime);

        return Slots.Any(slot => slot.Day == local.DayOfWeek && time >= slot.Opens && time < slot.Closes);
    }
}

public sealed record BusinessHoursSlot(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);

/// <summary>"Fuera de horario puede contestar el agente o dejarse en cola" — both halves.</summary>
public enum OutsideHoursBehavior
{
    /// <summary>The assistant keeps answering; hours only describe when the team is around.</summary>
    AssistantAnswers,

    /// <summary>The assistant stays quiet and the conversation waits for the team.</summary>
    LeaveForTeam,
}
