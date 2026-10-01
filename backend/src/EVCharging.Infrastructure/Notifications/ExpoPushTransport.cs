using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EVCharging.Application.Notifications;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Notifications;

public sealed class PushOptions
{
    public bool Enabled { get; set; }
    public string? AccessToken { get; set; }
    public int MaxAttempts { get; set; } = 5;
}

public sealed class ExpoPushTransport(HttpClient http, IOptions<PushOptions> options) : IPushTransport
{
    public async Task<PushResult> SendAsync(PushMessage message, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://exp.host/--/api/v2/push/send");
        if (!string.IsNullOrWhiteSpace(options.Value.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AccessToken);
        request.Content = JsonContent.Create(new
        {
            to = message.Token, title = message.Text.Title, body = message.Text.Body, sound = "default",
            data = new { notificationId = message.NotificationId }, channelId = "charging"
        });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new(false, (int)response.StatusCode >= 500 || (int)response.StatusCode == 429, null, "push_provider_http_error");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object) return new(false, true, null, "push_provider_invalid_response");
        var status = StringValue(data, "status");
        var ticketId = StringValue(data, "id");
        if (status == "ok" && !string.IsNullOrWhiteSpace(ticketId)) return new(true, false, ticketId, null);
        if (status != "error") return new(false, true, null, "push_provider_invalid_response");
        var error = ErrorValue(data, "push_rejected");
        return new(false, error == "MessageRateExceeded", null, error);
    }

    public async Task<PushReceipt> CheckReceiptAsync(string ticketId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://exp.host/--/api/v2/push/getReceipts");
        if (!string.IsNullOrWhiteSpace(options.Value.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AccessToken);
        request.Content = JsonContent.Create(new { ids = new[] { ticketId } });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return new(true, false, true, "push_receipt_unavailable");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(ticketId, out var receipt))
            return new(true, false, true, null);
        var status = StringValue(receipt, "status");
        if (status == "ok") return new(false, true, false, null);
        if (status != "error") return new(true, false, true, "push_provider_invalid_response");
        var error = ErrorValue(receipt, "push_receipt_failed");
        return new(false, false, error == "MessageRateExceeded", error);
    }

    private static string? StringValue(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

    private static string ErrorValue(JsonElement value, string fallback) =>
        value.TryGetProperty("details", out var details) ? StringValue(details, "error") ?? fallback : fallback;
}
