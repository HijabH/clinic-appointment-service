using System.Text.Json.Serialization;

namespace ClinicAppointments.Domain;

public sealed record Patient(string Id, string Name);

public sealed record Practitioner(
    string Id,
    string Name,
    TimeOnly WorkStart,
    TimeOnly WorkEnd,
    IReadOnlySet<DayOfWeek> WorkDays);

public enum AppointmentStatus
{
    Booked,
    Cancelled
}

public sealed record Appointment(
    string Id,
    string PatientId,
    string PractitionerId,
    DateTime Start,
    DateTime End,
    string Reason,
    AppointmentStatus Status,
    string? CancellationReason = null)
{
    [JsonIgnore]
    public bool IsActive => Status == AppointmentStatus.Booked;

    // Half-open intervals, so back-to-back appointments do not overlap.
    public bool Overlaps(DateTime start, DateTime end) => Start < end && start < End;
}

public sealed record AuditEntry(DateTime At, string Action, string AppointmentId, string Details);
