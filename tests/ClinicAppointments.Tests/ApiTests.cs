using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicAppointments.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicAppointments.Tests;

public sealed class TestApi : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }

    private TestApi(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public static async Task<TestApi> StartAsync()
    {
        var app = ApiApp.Build(TestData.CreateService());
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TestApi(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class ApiTests
{
    private static readonly object Booking = new
    {
        patientId = "P1",
        practitionerId = "D1",
        start = "2026-12-01T09:00:00",
        durationMinutes = 20,
        reason = "Check-up"
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Health_returns_ok()
    {
        await using var api = await TestApi.StartAsync();
        var json = await Json(await api.Client.GetAsync("/health"));
        Assert.Equal("ok", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Booking_returns_201_and_the_appointment()
    {
        await using var api = await TestApi.StartAsync();
        var response = await api.Client.PostAsJsonAsync("/appointments", Booking);
        var json = await Json(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("A1", json.GetProperty("id").GetString());
        Assert.Equal("Booked", json.GetProperty("status").GetString());
        Assert.False(json.TryGetProperty("isActive", out _));
        Assert.Equal("2026-12-01T09:20:00", json.GetProperty("end").GetString());
    }

    [Fact]
    public async Task Double_booking_returns_409_with_a_message()
    {
        await using var api = await TestApi.StartAsync();
        await api.Client.PostAsJsonAsync("/appointments", Booking);
        var response = await api.Client.PostAsJsonAsync("/appointments", new
        {
            patientId = "P2", practitionerId = "D1", start = "2026-12-01T09:10:00", durationMinutes = 20
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already booked", (await Json(response)).GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task Rule_violations_and_missing_fields_return_400()
    {
        await using var api = await TestApi.StartAsync();
        var badDuration = await api.Client.PostAsJsonAsync("/appointments", new
        {
            patientId = "P1", practitionerId = "D1", start = "2026-12-01T09:00:00", durationMinutes = 7
        });
        var missing = await api.Client.PostAsJsonAsync("/appointments", new { patientId = "P1" });
        var malformed = await api.Client.PostAsync("/appointments",
            new StringContent("{bad", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, badDuration.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Contains("Invalid request", (await Json(malformed)).GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task Unknown_ids_return_404()
    {
        await using var api = await TestApi.StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync("/appointments/A99")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync("/patients/P9/appointments")).StatusCode);
    }

    [Fact]
    public async Task Cancel_then_audit_shows_both_changes()
    {
        await using var api = await TestApi.StartAsync();
        await api.Client.PostAsJsonAsync("/appointments", Booking);
        var cancel = await api.Client.DeleteAsync("/appointments/A1?reason=patient%20is%20ill");
        var audit = await Json(await api.Client.GetAsync("/audit"));

        Assert.Equal("patient is ill", (await Json(cancel)).GetProperty("cancellationReason").GetString());
        Assert.Equal(2, audit.GetArrayLength());
        Assert.Equal("CANCELLED", audit[1].GetProperty("action").GetString());
    }

    [Fact]
    public async Task Slots_endpoint_lists_free_start_times()
    {
        await using var api = await TestApi.StartAsync();
        await api.Client.PostAsJsonAsync("/appointments", Booking);
        var json = await Json(await api.Client.GetAsync("/practitioners/D1/slots?date=2026-12-01&minutes=60"));
        var slots = json.GetProperty("slots").EnumerateArray().Select(s => s.GetString()).ToList();

        Assert.Equal(new[] { "08:00", "10:00", "11:00", "12:00", "13:00" }, slots);
    }
}
