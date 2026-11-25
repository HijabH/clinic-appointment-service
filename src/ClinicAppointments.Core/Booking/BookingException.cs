namespace ClinicAppointments.Booking;

public enum BookingError
{
    NotFound,
    Invalid,
    Conflict
}

public sealed class BookingException(BookingError error, string message) : Exception(message)
{
    public BookingError Error { get; } = error;
}
