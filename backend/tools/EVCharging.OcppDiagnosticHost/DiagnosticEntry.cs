using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EVCharging.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace EVCharging.OcppDiagnosticHost;

// Disposable transport comparison, using the product endpoint and protocol handlers.
// This host never connects to Render or a production database.
internal static class DiagnosticEntry
{
    public static async Task Main(string[] args)
    {
        if (!args.Contains("--local-ocpp-test") && !args.Contains("--self-check"))
            throw new InvalidOperationException("Pass --local-ocpp-test or --self-check.");

        await using var source = new ApiFactory();
        await using var factory = source.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["AllowedHosts"] = "*" }
                    )
            )
        );
        if (args.Contains("--self-check"))
        {
            var webSocketClient = factory.Server.CreateWebSocketClient();
            webSocketClient.SubProtocols.Add("ocpp1.6");
            using var socket = await webSocketClient.ConnectAsync(
                new Uri("ws://local-comparison.trycloudflare.com/ws/cs/NKR2023072805"),
                CancellationToken.None
            );
            await CheckAsync(
                socket,
                "BootNotification",
                new
                {
                    chargePointModel = "BDCC220C220",
                    chargePointSerialNumber = "NKR2023072805",
                    chargePointVendor = "com.cnchargepoint",
                    firmwareVersion = "v4.8.0-d9_S35.00.31.3.cb11200000",
                }
            );
            for (var connector = 0; connector <= 2; connector++)
                await CheckAsync(
                    socket,
                    "StatusNotification",
                    new
                    {
                        connectorId = connector,
                        status = "Available",
                        errorCode = "NoError",
                        timestamp = DateTimeOffset.UtcNow,
                    }
                );
            await CheckAsync(socket, "Heartbeat", new { });
            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Self-check completed",
                CancellationToken.None
            );
            Console.WriteLine(
                "PASS in-process comparison. TCP, TLS and Cloudflare are not covered."
            );
            return;
        }
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 9000));
        using var client = factory.CreateClient();
        Console.WriteLine("OCPP diagnostic host: http://127.0.0.1:9000/ws/cs/NKR2023072805");
        Console.WriteLine(
            "Same API WebSocket endpoint and OCPP handlers; temporary in-memory storage."
        );
        Console.WriteLine(
            "Data is lost when this host stops. This does not test Render/PostgreSQL."
        );
        Console.WriteLine("Cloudflare origin: http://127.0.0.1:9000 (start tunnel separately).");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private static async Task CheckAsync(WebSocket socket, string action, object payload)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var callId = Guid.NewGuid().ToString();
        var frame = JsonSerializer.SerializeToUtf8Bytes(
            new object[] { 2, callId, action, payload }
        );
        await socket.SendAsync(frame, WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        using var reply = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count));
        if (reply.RootElement[0].GetInt32() != 3 || reply.RootElement[1].GetString() != callId)
            throw new InvalidOperationException($"Unexpected reply to {action}.");
        if (
            action == "BootNotification"
            && reply.RootElement[2].GetProperty("status").GetString() != "Accepted"
        )
            throw new InvalidOperationException("Boot rejected.");
        Console.WriteLine($"PASS {action}");
    }
}
