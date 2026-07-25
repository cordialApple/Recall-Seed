using System.Net;
using System.Net.Sockets;
using System.Text;
using Recall_Seed.Scroll;

namespace Recall_Seed.Tests;

public sealed class VerdictReceiverTests : IAsyncLifetime
{
    VerdictReceiver _receiver = null!;
    VerdictLog _log = null!;
    int _port;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task InitializeAsync()
    {
        _port = FreeLoopbackPort();
        _log = new VerdictLog();
        _receiver = new VerdictReceiver(_port, _log, TextWriter.Null);
        await _receiver.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _receiver.StopAsync(CancellationToken.None);
        _http.Dispose();
    }

    Uri Url => new($"http://127.0.0.1:{_port}/");

    static int FreeLoopbackPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Post_of_a_golden_verdict_is_accepted_and_logged()
    {
        const string golden = "{\"endpointId\":\"ep_golden\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":2,\"total\":2,\"at\":1700000000000}";
        var res = await _http.PostAsync(Url, new StringContent(golden, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var v = _log.Latest("ep_golden");
        Assert.NotNull(v);
        Assert.Equal("pass", v!.Status);
        Assert.Equal(2, v.Total);
    }

    [Fact]
    public async Task Malformed_body_is_rejected_400_and_not_logged()
    {
        var res = await _http.PostAsync(Url, new StringContent("{not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0, _log.Count);
    }

    [Fact]
    public async Task Oversized_body_is_rejected_413_and_not_logged()
    {
        var big = "{\"endpointId\":\"" + new string('x', 5000) + "\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":1,\"total\":1,\"at\":1}";
        var res = await _http.PostAsync(Url, new StringContent(big, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        Assert.Equal(0, _log.Count);
    }

    [Fact]
    public async Task Non_post_method_is_rejected_405()
    {
        var res = await _http.GetAsync(Url);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, res.StatusCode);
        Assert.Equal(0, _log.Count);
    }
}
