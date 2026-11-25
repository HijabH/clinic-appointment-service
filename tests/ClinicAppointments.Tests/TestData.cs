using ClinicAppointments.Booking;
using ClinicAppointments.Domain;

namespace ClinicAppointments.Tests;

public sealed class FixedTimeProvider(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public static class TestData
{
    // Monday 30 November 2026, 10:00
    public static readonly DateTime Now = new(2026, 11, 30, 10, 0, 0);
    public static readonly DateTime Tuesday = new(2026, 12, 1);
    public static readonly DateTime Saturday = new(2026, 12, 5);

    public static AppointmentService CreateService()
    {
        var service = new AppointmentService(new FixedTimeProvider(Now));
        var weekdays = new HashSet<DayOfWeek>
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
        };
        service.AddPractitioner(new Practitioner("D1", "Dr. Anna Beispiel", new TimeOnly(8, 0), new TimeOnly(14, 0), weekdays));
        service.AddPractitioner(new Practitioner("D2", "Dr. Jonas Muster", new TimeOnly(12, 0), new TimeOnly(18, 0), weekdays));
        service.AddPatient(new Patient("P1", "Erika Mustermann"));
        service.AddPatient(new Patient("P2", "Max Mustermann"));
        return service;
    }

    public static DateTime At(this DateTime day, int hour, int minute = 0) => day.Date.AddHours(hour).AddMinutes(minute);
}
