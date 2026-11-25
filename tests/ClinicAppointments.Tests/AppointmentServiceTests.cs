using ClinicAppointments.Booking;
using ClinicAppointments.Domain;

namespace ClinicAppointments.Tests;

public class AppointmentServiceTests
{
    private readonly AppointmentService _service = TestData.CreateService();

    private static BookingError ErrorOf(Action action) => Assert.Throws<BookingException>(action).Error;

    [Fact]
    public void Books_a_valid_appointment()
    {
        var a = _service.Book("P1", "D1", TestData.Tuesday.At(9), 20, " Check-up ");

        Assert.Equal("A1", a.Id);
        Assert.Equal(TestData.Tuesday.At(9, 20), a.End);
        Assert.Equal("Check-up", a.Reason);
        Assert.Equal(AppointmentStatus.Booked, a.Status);
    }

    [Fact]
    public void Unknown_patient_or_practitioner_is_not_found()
    {
        Assert.Equal(BookingError.NotFound, ErrorOf(() => _service.Book("P9", "D1", TestData.Tuesday.At(9), 20, null)));
        Assert.Equal(BookingError.NotFound, ErrorOf(() => _service.Book("P1", "D9", TestData.Tuesday.At(9), 20, null)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(125)]
    [InlineData(22)]
    public void Duration_must_be_10_to_120_minutes_in_5_minute_steps(int minutes)
    {
        Assert.Equal(BookingError.Invalid, ErrorOf(() => _service.Book("P1", "D1", TestData.Tuesday.At(9), minutes, null)));
    }

    [Fact]
    public void Start_must_be_on_a_5_minute_mark()
    {
        Assert.Equal(BookingError.Invalid, ErrorOf(() => _service.Book("P1", "D1", TestData.Tuesday.At(9, 3), 20, null)));
    }

    [Fact]
    public void Appointments_in_the_past_are_rejected()
    {
        Assert.Equal(BookingError.Invalid, ErrorOf(() => _service.Book("P1", "D1", TestData.Now.At(9), 20, null)));
        Assert.Equal(BookingError.Invalid, ErrorOf(() => _service.Book("P1", "D1", TestData.Now, 20, null)));
    }

    [Fact]
    public void Appointment_must_be_inside_working_hours_and_days()
    {
        Assert.Equal(BookingError.Invalid, ErrorOf(() => _service.Book("P1", "D1", TestData.Tuesday.At(7, 50), 20, null)));
        Assert.Equal(BookingError.Invalid, ErrorOf(() => _service.Book("P1", "D1", TestData.Tuesday.At(13, 50), 20, null)));
        Assert.Equal(BookingError.Invalid, ErrorOf(() => _service.Book("P1", "D1", TestData.Saturday.At(9), 20, null)));
    }

    [Fact]
    public void Appointment_may_end_exactly_when_work_ends()
    {
        var a = _service.Book("P1", "D1", TestData.Tuesday.At(13, 40), 20, null);
        Assert.Equal(TestData.Tuesday.At(14), a.End);
    }

    [Fact]
    public void Practitioner_cannot_be_double_booked()
    {
        _service.Book("P1", "D1", TestData.Tuesday.At(9), 20, null);
        var e = Assert.Throws<BookingException>(() => _service.Book("P2", "D1", TestData.Tuesday.At(9, 10), 20, null));

        Assert.Equal(BookingError.Conflict, e.Error);
        Assert.Contains("Dr. Anna Beispiel", e.Message);
    }

    [Fact]
    public void Patient_cannot_be_in_two_places_at_once()
    {
        _service.Book("P1", "D1", TestData.Tuesday.At(12), 30, null);
        Assert.Equal(BookingError.Conflict, ErrorOf(() => _service.Book("P1", "D2", TestData.Tuesday.At(12, 15), 20, null)));
    }

    [Fact]
    public void Back_to_back_appointments_are_allowed()
    {
        _service.Book("P1", "D1", TestData.Tuesday.At(9), 20, null);
        var second = _service.Book("P2", "D1", TestData.Tuesday.At(9, 20), 20, null);
        Assert.Equal("A2", second.Id);
    }

    [Fact]
    public void Cancelling_frees_the_slot()
    {
        var a = _service.Book("P1", "D1", TestData.Tuesday.At(9), 20, null);
        var cancelled = _service.Cancel(a.Id, " patient is ill ");

        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.Equal("patient is ill", cancelled.CancellationReason);
        Assert.Equal("A2", _service.Book("P2", "D1", TestData.Tuesday.At(9), 20, null).Id);
    }

    [Fact]
    public void Cancelled_appointment_cannot_be_cancelled_again()
    {
        var a = _service.Book("P1", "D1", TestData.Tuesday.At(9), 20, null);
        _service.Cancel(a.Id, null);
        Assert.Equal(BookingError.Conflict, ErrorOf(() => _service.Cancel(a.Id, null)));
    }

    [Fact]
    public void Past_appointment_cannot_be_cancelled()
    {
        var clock = new MutableTimeProvider(TestData.Now);
        var service = new AppointmentService(clock);
        service.AddPatient(new Patient("P1", "Erika Mustermann"));
        service.AddPractitioner(new Practitioner("D1", "Dr. Anna Beispiel", new TimeOnly(8, 0), new TimeOnly(14, 0),
            new HashSet<DayOfWeek> { DayOfWeek.Monday }));
        var booked = service.Book("P1", "D1", TestData.Now.At(11), 20, null);

        clock.Now = TestData.Now.At(12);

        Assert.Equal(BookingError.Invalid, ErrorOf(() => service.Cancel(booked.Id, null)));
    }

    [Fact]
    public void Unknown_appointment_is_not_found()
    {
        Assert.Equal(BookingError.NotFound, ErrorOf(() => _service.Find("A99")));
        Assert.Equal(BookingError.NotFound, ErrorOf(() => _service.Cancel("A99", null)));
    }

    [Fact]
    public void Patient_appointments_are_returned_in_time_order()
    {
        _service.Book("P1", "D1", TestData.Tuesday.At(11), 20, null);
        _service.Book("P1", "D1", TestData.Tuesday.At(9), 20, null);
        _service.Book("P2", "D1", TestData.Tuesday.At(10), 20, null);

        var list = _service.ForPatient("P1");
        Assert.Equal(new[] { "A2", "A1" }, list.Select(a => a.Id));
        Assert.Equal(BookingError.NotFound, ErrorOf(() => _service.ForPatient("P9")));
    }

    [Fact]
    public void Free_slots_skip_booked_times()
    {
        _service.Book("P1", "D1", TestData.Tuesday.At(8, 30), 30, null);
        var slots = _service.FreeSlots("D1", DateOnly.FromDateTime(TestData.Tuesday), 30);

        Assert.Equal(11, slots.Count);
        Assert.Equal(new TimeOnly(8, 0), slots[0]);
        Assert.DoesNotContain(new TimeOnly(8, 30), slots);
        Assert.Equal(new TimeOnly(13, 30), slots[^1]);
    }

    [Fact]
    public void Free_slots_skip_past_times_and_days_off()
    {
        var today = _service.FreeSlots("D1", DateOnly.FromDateTime(TestData.Now), 60);
        Assert.Equal(new[] { new TimeOnly(11, 0), new TimeOnly(12, 0), new TimeOnly(13, 0) }, today);
        Assert.Empty(_service.FreeSlots("D1", DateOnly.FromDateTime(TestData.Saturday), 30));
    }

    [Fact]
    public void Every_change_is_written_to_the_audit_log()
    {
        var a = _service.Book("P1", "D1", TestData.Tuesday.At(9), 20, null);
        _service.Cancel(a.Id, "patient is ill");

        var log = _service.AuditLog();
        Assert.Equal(new[] { "BOOKED", "CANCELLED" }, log.Select(e => e.Action));
        Assert.Equal("P1 with D1 at 2026-12-01T09:00", log[0].Details);
        Assert.Equal("patient is ill", log[1].Details);
        Assert.Equal(TestData.Now, log[0].At);
    }

    [Fact]
    public async Task Parallel_bookings_for_the_same_slot_succeed_only_once()
    {
        for (var i = 3; i <= 20; i++)
            _service.AddPatient(new Patient($"P{i}", $"Patient {i}"));

        var attempts = Enumerable.Range(1, 20).Select(i => Task.Run(() =>
        {
            try
            {
                _service.Book($"P{i}", "D1", TestData.Tuesday.At(9), 20, null);
                return true;
            }
            catch (BookingException)
            {
                return false;
            }
        }));

        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(ok => ok));
        Assert.Single(_service.AuditLog());
    }
}

public sealed class MutableTimeProvider(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
