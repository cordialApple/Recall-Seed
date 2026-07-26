using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Recall_Seed.Scroll;

/// <summary>
/// Loopback HTTP sink for contract-6 verdicts. Binds http://127.0.0.1:{port}/ — a specific loopback IP,
/// never a public interface — accepts a POST with a small JSON body, strict-parses it, and appends to a
/// <see cref="VerdictLog"/>. The peer is fire-and-forget, so anything malformed/oversized/wrong-method
/// gets a 4xx and is dropped; a handler fault never takes down the accept loop.
/// </summary>
internal sealed class VerdictReceiver : IHostedService
{
    const int MaxBodyBytes = 4096;

    readonly int _port;
    readonly VerdictLog _log;
    readonly TextWriter _err;
    readonly HttpListener _listener = new();
    CancellationTokenSource? _cts;
    Task? _loop;

    public VerdictReceiver(int port, VerdictLog log, TextWriter err)
    {
        _port = port;
        _log = log;
        _err = err;
    }

    public static void Register(IServiceCollection services, TextWriter err)
    {
        var env = new Dictionary<string, string?>
        {
            [ScrollReceiverConfig.PortEnv] = Environment.GetEnvironmentVariable(ScrollReceiverConfig.PortEnv),
        };
        var port = ScrollReceiverConfig.ResolvePort(env);
        if (port is null)
        {
            err.WriteLine($"[Recall_Seed] scroll verdict receiver: disabled (set {ScrollReceiverConfig.PortEnv} to enable)");
            return;
        }
        services.AddHostedService(_ => new VerdictReceiver(port.Value, VerdictLog.Shared, err));
        err.WriteLine($"[Recall_Seed] scroll verdict receiver: listening on http://127.0.0.1:{port}/");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = AcceptLoop(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        if (_loop is not null)
        {
            try { await _loop; } catch { }
        }
        try { _listener.Close(); } catch { }
    }

    async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch when (ct.IsCancellationRequested) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = HandleAsync(ctx);
        }
    }

    async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            // Scroll's contract-6 POST is a cross-origin browser fetch (application/json => non-simple),
            // so the browser sends a CORS preflight first; answer it or the real POST never leaves the tab.
            if (string.Equals(ctx.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                Respond(ctx, 204);
                return;
            }

            if (!string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                Respond(ctx, 405);
                return;
            }

            var (ok, body, tooBig) = await ReadCapped(ctx.Request.InputStream);
            if (tooBig) { Respond(ctx, 413); return; }
            if (!ok) { Respond(ctx, 400); return; }

            if (ScrollVerdict.TryParse(body, out var verdict, out var error))
            {
                var correlation = ctx.Request.QueryString["c"];
                if (!string.IsNullOrEmpty(correlation)) verdict = verdict! with { Correlation = correlation };
                _log.Add(verdict!);
                Respond(ctx, 204);
            }
            else
            {
                _err.WriteLine($"[Recall_Seed] scroll verdict rejected: {error}");
                Respond(ctx, 400);
            }
        }
        catch
        {
            Respond(ctx, 500);
        }
    }

    static async Task<(bool ok, ReadOnlyMemory<byte> body, bool tooBig)> ReadCapped(Stream input)
    {
        var buf = new byte[MaxBodyBytes + 1];
        var total = 0;
        int n;
        while (total < buf.Length && (n = await input.ReadAsync(buf.AsMemory(total, buf.Length - total))) > 0)
        {
            total += n;
            if (total > MaxBodyBytes) return (false, default, true);
        }
        return (true, buf.AsMemory(0, total), false);
    }

    static void Respond(HttpListenerContext ctx, int status)
    {
        try
        {
            var h = ctx.Response.Headers;
            h["Access-Control-Allow-Origin"] = "*";
            h["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            h["Access-Control-Allow-Headers"] = "content-type";
            ctx.Response.StatusCode = status;
            ctx.Response.Close();
        }
        catch { }
    }
}
