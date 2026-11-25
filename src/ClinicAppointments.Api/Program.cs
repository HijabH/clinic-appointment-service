using ClinicAppointments.Api;
using ClinicAppointments.Booking;
using ClinicAppointments.Domain;

var service = new AppointmentService(TimeProvider.System);

var weekdays = new HashSet<DayOfWeek>
{
    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
};
service.AddPractitioner(new Practitioner("D1", "Dr. Anna Beispiel", new TimeOnly(8, 0), new TimeOnly(14, 0), weekdays));
service.AddPractitioner(new Practitioner("D2", "Dr. Jonas Muster", new TimeOnly(12, 0), new TimeOnly(18, 0), weekdays));
service.AddPatient(new Patient("P1", "Erika Mustermann"));
service.AddPatient(new Patient("P2", "Max Mustermann"));

var app = ApiApp.Build(service, args);
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");
Console.WriteLine($"Clinic appointment service listening on port {port}");
app.Run();
