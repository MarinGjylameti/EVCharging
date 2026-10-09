using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace EVCharging.Tests;

public sealed class CloudflareOriginHostTests
{
    [Fact]
    public async Task LoopbackHostRewriteAllowsHealthAndOcppThroughTheTunnel()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        const string publicHost = "generated-charger-test.trycloudflare.com";

        using var publicRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://{publicHost}/health/ready"
        );
        using var rejected = await client.SendAsync(publicRequest, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        // cloudflared --http-host-header changes the local hop's Host, including upgrades.
        using var rewrittenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://{publicHost}/health/ready"
        );
        rewrittenRequest.Headers.Host = "127.0.0.1";
        using var ready = await client.SendAsync(rewrittenRequest, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        var websocket = factory.Server.CreateWebSocketClient();
        websocket.SubProtocols.Add("ocpp1.6");
        websocket.ConfigureRequest = request => request.Headers.Host = "127.0.0.1";
        using var socket = await websocket.ConnectAsync(
            new Uri($"ws://{publicHost}/ws/cs/CP-TUNNEL-HOST"),
            timeout.Token
        );
        Assert.Equal("ocpp1.6", socket.SubProtocol);
        var boot = Encoding.UTF8.GetBytes(
            "[2,\"tunnel-boot\",\"BootNotification\",{\"chargePointVendor\":\"LocalTest\",\"chargePointModel\":\"HostRewrite\"}]"
        );
        await socket.SendAsync(boot, WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.True(received.EndOfMessage);
        Assert.Equal(WebSocketMessageType.Text, received.MessageType);
        using var reply = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
        Assert.Equal(3, reply.RootElement[0].GetInt32());
        Assert.Equal("tunnel-boot", reply.RootElement[1].GetString());
        Assert.Equal("Accepted", reply.RootElement[2].GetProperty("status").GetString());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }
}
