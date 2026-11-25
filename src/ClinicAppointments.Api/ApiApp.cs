using System.Text.Json.Serialization;
using ClinicAppointments.Booking;

namespace ClinicAppointments.Api;

public sealed record BookingRequest(
    string? PatientId,
    string? PractitionerId,
    DateTime? Start,
    int? DurationMinutes,
    string? Reason);

public static class ApiApp
{
    public static WebApplication Build(AppointmentService service, string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(service);
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (BookingException e)
            {
                context.Response.StatusCode = e.Error switch
                {
                    BookingError.NotFound => StatusCodes.Status404NotFound,
                    BookingError.Conflict => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest
                };
                await context.Response.WriteAsJsonAsync(new { error = e.Message });
            }
            catch (BadHttpRequestException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid request body or parameters" });
            }
        });

        app.MapGet("/health", () => new { status = "ok" });

        app.MapGet("/practitioners", (AppointmentService s) => s.Practitioners().Select(p => new
        {
            p.Id,
            p.Name,
            workStart = p.WorkStart.ToString("HH:mm"),
            workEnd = p.WorkEnd.ToString("HH:mm"),
            workDays = p.WorkDays.OrderBy(d => ((int)d + 6) % 7).Select(d => d.ToString())
        }));

        app.MapGet("/practitioners/{id}/slots", (string id, DateOnly date, int? minutes, AppointmentService s) =>
        {
            var duration = minutes ?? 20;
            return new
            {
                practitionerId = id,
                date = date.ToString("yyyy-MM-dd"),
                minutes = duration,
                slots = s.FreeSlots(id, date, duration).Select(t => t.ToString("HH:mm"))
            };
        });

        app.MapPost("/appointments", (BookingRequest request, AppointmentService s) =>
        {
            if (string.IsNullOrWhiteSpace(request.PatientId) || string.IsNullOrWhiteSpace(request.PractitionerId)
                || request.Start is null || request.DurationMinutes is null)
            {
                throw new BookingException(BookingError.Invalid,
                    "patientId, practitionerId, start and durationMinutes are required");
            }
            var appointment = s.Book(request.PatientId, request.PractitionerId, request.Start.Value,
                request.DurationMinutes.Value, request.Reason);
            return Results.Created($"/appointments/{appointment.Id}", appointment);
        });

        app.MapGet("/appointments/{id}", (string id, AppointmentService s) => s.Find(id));

        app.MapDelete("/appointments/{id}", (string id, string? reason, AppointmentService s) => s.Cancel(id, reason));

        app.MapGet("/patients/{id}/appointments", (string id, AppointmentService s) => s.ForPatient(id));

        app.MapGet("/audit", (AppointmentService s) => s.AuditLog());

        return app;
    }
}
