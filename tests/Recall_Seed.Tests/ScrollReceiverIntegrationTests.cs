using System.Net;
using System.Net.Sockets;
using System.Text;
using Recall_Seed.Scroll;

namespace Recall_Seed.Tests;

public class ScrollReceiverIntegrationTests
{
    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Contract6_post_reaches_the_receiver_and_reads_back_by_spawn_id()
    {
        var port = FreePort();
        var receiver = new VerdictReceiver(port, VerdictLog.Shared, TextWriter.Null);
        await receiver.StartAsync(default);
        try
        {
            var corr = "corr-int-" + Guid.NewGuid().ToString("N");
            var callback = ScrollSpawnConfig.CallbackUrl(port, corr);
            var payload = "{\"endpointId\":\"ep_live\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":2,\"total\":2,\"at\":1700000000000}";

            using var http = new HttpClient();
            var res = await http.PostAsync(callback, new StringContent(payload, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

            var read = ScrollVerdictTools.GetScrollVerdict(corr);
            var v = Assert.Single(read.Verdicts);
            Assert.Equal(corr, v.SpawnId);        // keyed by the correlation we baked into the URL
            Assert.Equal("ep_live", v.EndpointId); // browser-minted id rode in the payload
            Assert.Equal("pass", v.Status);
            Assert.Equal(2, v.Total);

            var wrongMethod = await http.GetAsync($"http://127.0.0.1:{port}/?c={corr}");
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);

            Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }
        finally
        {
            await receiver.StopAsync(default);
        }
    }

    [Fact]
    public async Task Answers_the_cors_preflight_so_the_browser_will_send_the_post()
    {
        var port = FreePort();
        var receiver = new VerdictReceiver(port, VerdictLog.Shared, TextWriter.Null);
        await receiver.StartAsync(default);
        try
        {
            using var http = new HttpClient();
            var pre = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/?c=x");
            pre.Headers.Add("Origin", "http://localhost:5173");
            pre.Headers.Add("Access-Control-Request-Method", "POST");
            pre.Headers.Add("Access-Control-Request-Headers", "content-type");

            var res = await http.SendAsync(pre);
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
            Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Contains("POST", res.Headers.GetValues("Access-Control-Allow-Methods").Single());
            Assert.Contains("content-type", res.Headers.GetValues("Access-Control-Allow-Headers").Single());
        }
        finally
        {
            await receiver.StopAsync(default);
        }
    }
}
