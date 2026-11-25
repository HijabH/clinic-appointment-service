# Clinic Appointment Service

![build-and-test](https://github.com/HijabH/clinic-appointment-service/actions/workflows/ci.yml/badge.svg)

A REST service in C# and ASP.NET Core (.NET 8) for booking clinic appointments: practitioners with working hours, patients, bookings, cancellations, free-slot search and an audit log.

Scheduling is a small domain with real backend problems: business rules that must never be broken, two people trying to book the same slot at the same moment, and a record of every change, which clinical systems need for accountability.

## Booking rules

- The patient and the practitioner must exist
- Appointments are 10 to 120 minutes long, in 5-minute steps, and start on a 5-minute mark
- They must be in the future and fully inside the practitioner's working hours and days
- A practitioner cannot be double-booked, and a patient cannot be in two places at once
- Back-to-back appointments are allowed (09:00-09:20 and 09:20-09:40 do not overlap)
- Cancelled appointments free their slot; past or already cancelled appointments cannot be cancelled
- Every booking and cancellation is written to the audit log

The overlap check and the save happen under one lock, so when many requests try to book the same slot at once, exactly one succeeds. A test runs 20 parallel bookings to prove it.

## API

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/health` | Status |
| `GET` | `/practitioners` | Practitioners and their working hours |
| `GET` | `/practitioners/{id}/slots?date=2027-03-02&minutes=20` | Free start times on a day |
| `POST` | `/appointments` | Book an appointment |
| `GET` | `/appointments/{id}` | One appointment |
| `DELETE` | `/appointments/{id}?reason=...` | Cancel an appointment |
| `GET` | `/patients/{id}/appointments` | A patient's appointments in time order |
| `GET` | `/audit` | All changes |

Broken booking rules return `400` (invalid request), `404` (unknown patient, practitioner or appointment) or `409` (conflict with an existing booking), always with a JSON error message.

## Run it

Requires the .NET 8 SDK.

```bash
dotnet test                                        # build and run the tests
dotnet run --project src/ClinicAppointments.Api
```

The server starts on port 8080 (set `PORT` to change it) with two fictional practitioners (`D1`, `D2`) and two patients (`P1`, `P2`).

```text
$ curl -X POST localhost:8080/appointments -H "Content-Type: application/json" \
    -d '{"patientId":"P1","practitionerId":"D1","start":"2027-03-02T09:00","durationMinutes":20,"reason":"Check-up"}'
{"id":"A1","patientId":"P1","practitionerId":"D1","start":"2027-03-02T09:00:00","end":"2027-03-02T09:20:00","reason":"Check-up","status":"Booked"}

$ curl -X POST localhost:8080/appointments -H "Content-Type: application/json" \
    -d '{"patientId":"P2","practitionerId":"D1","start":"2027-03-02T09:10","durationMinutes":20}'
{"error":"Dr. Anna Beispiel is already booked at that time"}          (HTTP 409)

$ curl "localhost:8080/practitioners/D1/slots?date=2027-03-02&minutes=60"
{"practitionerId":"D1","date":"2027-03-02","minutes":60,"slots":["08:00","10:00","11:00","12:00","13:00"]}

$ curl -X DELETE "localhost:8080/appointments/A1?reason=patient%20is%20ill"
{"id":"A1", ... ,"status":"Cancelled","cancellationReason":"patient is ill"}
```

## Project structure

| Path | Responsibility |
| --- | --- |
| `src/ClinicAppointments.Core/Domain` | Immutable records: `Patient`, `Practitioner`, `Appointment`, `AuditEntry` |
| `src/ClinicAppointments.Core/Booking` | `AppointmentService` with the booking rules, cancellation, free slots and audit log |
| `src/ClinicAppointments.Api` | ASP.NET Core minimal API, mapping rule violations to HTTP status codes |
| `tests/ClinicAppointments.Tests` | xUnit tests for the rules and the HTTP API |

Design choices:
- **Time is injected** through .NET's `TimeProvider`, so tests can fix "now" and check rules such as "no bookings in the past" reliably.
- **Records for the domain**, so appointments are immutable; a cancellation creates a new record with `with`.
- **Rule violations are typed** (`BookingException` with `NotFound`, `Invalid`, `Conflict`) and the API maps them to status codes in one middleware.
- **The booking rules live in a class library** with no web dependency, so they can be reused or moved behind another interface.

## Tests

```bash
dotnet test
```

28 xUnit tests cover every booking rule, cancellations, free slots, ordering, the audit log, the parallel-booking race and the HTTP API end to end (the app is started on a free port and called with `HttpClient`). GitHub Actions runs them on every push.

## Limitations and next steps

- Data is kept in memory; an Entity Framework Core repository with SQL Server or PostgreSQL would be the next step
- No authentication yet; a real clinic system would need roles for patients, reception and doctors
- Time zones are not handled; all times are local clinic time

## License

[MIT](LICENSE)
