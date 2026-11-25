using ClinicAppointments.Domain;

namespace ClinicAppointments.Booking;

public sealed class AppointmentService(TimeProvider time)
{
    public const int MinMinutes = 10;
    public const int MaxMinutes = 120;
    public const int StepMinutes = 5;

    private readonly object _bookingLock = new();
    private readonly Dictionary<string, Patient> _patients = new();
    private readonly Dictionary<string, Practitioner> _practitioners = new();
    private readonly Dictionary<string, Appointment> _appointments = new();
    private readonly List<AuditEntry> _audit = new();
    private int _nextId = 1;

    private DateTime Now => time.GetLocalNow().DateTime;

    public void AddPatient(Patient patient)
    {
        lock (_bookingLock) _patients[patient.Id] = patient;
    }

    public void AddPractitioner(Practitioner practitioner)
    {
        lock (_bookingLock) _practitioners[practitioner.Id] = practitioner;
    }

    public IReadOnlyList<Practitioner> Practitioners()
    {
        lock (_bookingLock) return _practitioners.Values.OrderBy(p => p.Id).ToList();
    }

    public Appointment Book(string patientId, string practitionerId, DateTime start, int durationMinutes, string? reason)
    {
        // Overlap check and save must happen under the same lock,
        // otherwise two parallel requests can both book the same slot.
        lock (_bookingLock)
        {
            if (!_patients.ContainsKey(patientId))
                throw new BookingException(BookingError.NotFound, $"Unknown patient {patientId}");
            var practitioner = GetPractitioner(practitionerId);

            ValidateDuration(durationMinutes);
            if (start.Minute % StepMinutes != 0 || start.Second != 0 || start.Millisecond != 0)
                throw new BookingException(BookingError.Invalid, "Appointments start on a 5-minute mark");
            if (start <= Now)
                throw new BookingException(BookingError.Invalid, "Appointments must be in the future");

            var end = start.AddMinutes(durationMinutes);
            if (!IsWorking(practitioner, start, end))
                throw new BookingException(BookingError.Invalid, $"{practitioner.Name} is not working at that time");

            foreach (var existing in _appointments.Values.Where(a => a.IsActive && a.Overlaps(start, end)))
            {
                if (existing.PractitionerId == practitionerId)
                    throw new BookingException(BookingError.Conflict, $"{practitioner.Name} is already booked at that time");
                if (existing.PatientId == patientId)
                    throw new BookingException(BookingError.Conflict, "The patient already has an appointment at that time");
            }

            var appointment = new Appointment($"A{_nextId++}", patientId, practitionerId, start, end,
                reason?.Trim() ?? "", AppointmentStatus.Booked);
            _appointments[appointment.Id] = appointment;
            WriteAudit("BOOKED", appointment.Id, $"{patientId} with {practitionerId} at {start:yyyy-MM-ddTHH:mm}");
            return appointment;
        }
    }

    public Appointment Cancel(string appointmentId, string? reason)
    {
        lock (_bookingLock)
        {
            var appointment = FindUnlocked(appointmentId);
            if (!appointment.IsActive)
                throw new BookingException(BookingError.Conflict, $"Appointment {appointmentId} is already cancelled");
            if (appointment.Start <= Now)
                throw new BookingException(BookingError.Invalid, "Past appointments cannot be cancelled");

            var cancelled = appointment with
            {
                Status = AppointmentStatus.Cancelled,
                CancellationReason = reason?.Trim() ?? ""
            };
            _appointments[appointmentId] = cancelled;
            WriteAudit("CANCELLED", appointmentId, cancelled.CancellationReason!);
            return cancelled;
        }
    }

    public Appointment Find(string appointmentId)
    {
        lock (_bookingLock) return FindUnlocked(appointmentId);
    }

    public IReadOnlyList<Appointment> ForPatient(string patientId)
    {
        lock (_bookingLock)
        {
            if (!_patients.ContainsKey(patientId))
                throw new BookingException(BookingError.NotFound, $"Unknown patient {patientId}");
            return _appointments.Values.Where(a => a.PatientId == patientId).OrderBy(a => a.Start).ToList();
        }
    }

    public IReadOnlyList<TimeOnly> FreeSlots(string practitionerId, DateOnly date, int durationMinutes)
    {
        lock (_bookingLock)
        {
            var practitioner = GetPractitioner(practitionerId);
            ValidateDuration(durationMinutes);

            var free = new List<TimeOnly>();
            if (!practitioner.WorkDays.Contains(date.DayOfWeek))
                return free;

            var booked = _appointments.Values.Where(a => a.IsActive && a.PractitionerId == practitionerId).ToList();
            var dayEnd = date.ToDateTime(practitioner.WorkEnd);

            for (var start = date.ToDateTime(practitioner.WorkStart);
                 start.AddMinutes(durationMinutes) <= dayEnd;
                 start = start.AddMinutes(durationMinutes))
            {
                var end = start.AddMinutes(durationMinutes);
                if (start > Now && !booked.Any(a => a.Overlaps(start, end)))
                    free.Add(TimeOnly.FromDateTime(start));
            }
            return free;
        }
    }

    public IReadOnlyList<AuditEntry> AuditLog()
    {
        lock (_bookingLock) return _audit.ToList();
    }

    private Practitioner GetPractitioner(string practitionerId) =>
        _practitioners.TryGetValue(practitionerId, out var practitioner)
            ? practitioner
            : throw new BookingException(BookingError.NotFound, $"Unknown practitioner {practitionerId}");

    private Appointment FindUnlocked(string appointmentId) =>
        _appointments.TryGetValue(appointmentId, out var appointment)
            ? appointment
            : throw new BookingException(BookingError.NotFound, $"Unknown appointment {appointmentId}");

    private static void ValidateDuration(int minutes)
    {
        if (minutes < MinMinutes || minutes > MaxMinutes || minutes % StepMinutes != 0)
            throw new BookingException(BookingError.Invalid, "Duration must be 10 to 120 minutes in 5-minute steps");
    }

    private static bool IsWorking(Practitioner p, DateTime start, DateTime end) =>
        start.Date == end.Date
        && p.WorkDays.Contains(start.DayOfWeek)
        && TimeOnly.FromDateTime(start) >= p.WorkStart
        && TimeOnly.FromDateTime(end) <= p.WorkEnd;

    private void WriteAudit(string action, string appointmentId, string details)
    {
        var now = Now;
        _audit.Add(new AuditEntry(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)), action, appointmentId, details));
    }
}
