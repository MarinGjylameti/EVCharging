using System.Net;
using System.Text;
using System.Text.Json;
using EVCharging.Application.Notifications;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace EVCharging.Tests;

// Exercise the production HTTP adapter with an in-process handler. No network,
// provider account or phone is used; actual push delivery remains a device check.
public sealed class ExpoPushTransportTests
{
    private static readonly PushMessage Message = new(
        "ExpoPushToken[test]",
        new NotificationText("Karikimi", "Përfundoi"),
        Guid.NewGuid()
    );

    [Fact]
    public async Task SendsLocalizedPayloadAndBearerTokenWithoutTreatingTicketAsDelivery()
    {
        using var http = new HttpClient(
            new Handler(
                async (request, ct) =>
                {
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal(
                        "https://exp.host/--/api/v2/push/send",
                        request.RequestUri!.AbsoluteUri
                    );
                    Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                    Assert.Equal("provider-test-token", request.Headers.Authorization.Parameter);
                    using var json = JsonDocument.Parse(
                        await request.Content!.ReadAsStringAsync(ct)
                    );
                    Assert.Equal(Message.Token, json.RootElement.GetProperty("to").GetString());
                    Assert.Equal(
                        Message.Text.Title,
                        json.RootElement.GetProperty("title").GetString()
                    );
                    Assert.Equal(
                        Message.NotificationId,
                        json.RootElement.GetProperty("data").GetProperty("notificationId").GetGuid()
                    );
                    return Reply(200, "{\"data\":{\"status\":\"ok\",\"id\":\"ticket-1\"}}");
                }
            )
        );
        var transport = new ExpoPushTransport(
            http,
            Options.Create(new PushOptions { AccessToken = "provider-test-token" })
        );
        Assert.Equal(
            new PushResult(true, false, "ticket-1", null),
            await transport.SendAsync(Message, default)
        );
    }

    [Theory]
    [InlineData(429, "{}", true, "push_provider_http_error")]
    [InlineData(503, "{}", true, "push_provider_http_error")]
    [InlineData(400, "{}", false, "push_provider_http_error")]
    [InlineData(200, "{}", true, "push_provider_invalid_response")]
    [InlineData(200, "[]", true, "push_provider_invalid_response")]
    [InlineData(200, "{\"data\":{\"status\":\"ok\"}}", true, "push_provider_invalid_response")]
    [InlineData(
        200,
        "{\"data\":{\"status\":\"error\",\"details\":{\"error\":\"MessageRateExceeded\"}}}",
        true,
        "MessageRateExceeded"
    )]
    [InlineData(
        200,
        "{\"data\":{\"status\":\"error\",\"details\":{\"error\":\"DeviceNotRegistered\"}}}",
        false,
        "DeviceNotRegistered"
    )]
    [InlineData(200, "{\"data\":{\"status\":\"error\"}}", false, "push_rejected")]
    public async Task TicketResponsesDistinguishTransientAndTerminalFailures(
        int status,
        string body,
        bool retryable,
        string error
    )
    {
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    Assert.Null(request.Headers.Authorization);
                    return Task.FromResult(Reply(status, body));
                }
            )
        );
        var transport = new ExpoPushTransport(http, Options.Create(new PushOptions()));
        Assert.Equal(
            new PushResult(false, retryable, null, error),
            await transport.SendAsync(Message, default)
        );
    }

    [Theory]
    [InlineData(200, "{\"data\":{\"ticket-1\":{\"status\":\"ok\"}}}", false, true, false, null)]
    [InlineData(200, "{\"data\":{}}", true, false, true, null)]
    [InlineData(
        200,
        "{\"data\":{\"ticket-1\":{\"status\":\"pending\"}}}",
        true,
        false,
        true,
        "push_provider_invalid_response"
    )]
    [InlineData(
        200,
        "{\"data\":{\"ticket-1\":{\"status\":\"error\",\"details\":{\"error\":\"DeviceNotRegistered\"}}}}",
        false,
        false,
        false,
        "DeviceNotRegistered"
    )]
    [InlineData(
        200,
        "{\"data\":{\"ticket-1\":{\"status\":\"error\",\"details\":{\"error\":\"MessageRateExceeded\"}}}}",
        false,
        false,
        true,
        "MessageRateExceeded"
    )]
    [InlineData(503, "{}", true, false, true, "push_receipt_unavailable")]
    public async Task ReceiptChecksDistinguishPendingDeliveredAndFailed(
        int status,
        string body,
        bool pending,
        bool delivered,
        bool retryable,
        string? error
    )
    {
        using var http = new HttpClient(
            new Handler(
                async (request, ct) =>
                {
                    Assert.Equal(
                        "https://exp.host/--/api/v2/push/getReceipts",
                        request.RequestUri!.AbsoluteUri
                    );
                    using var json = JsonDocument.Parse(
                        await request.Content!.ReadAsStringAsync(ct)
                    );
                    Assert.Equal("ticket-1", json.RootElement.GetProperty("ids")[0].GetString());
                    return Reply(status, body);
                }
            )
        );
        var transport = new ExpoPushTransport(http, Options.Create(new PushOptions()));
        Assert.Equal(
            new PushReceipt(pending, delivered, retryable, error),
            await transport.CheckReceiptAsync("ticket-1", default)
        );
    }

    private static HttpResponseMessage Reply(int status, string body) =>
        new((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => reply(request, ct);
    }
}
