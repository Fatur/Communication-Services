using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// Simple load tester for the messaging API
// Usage: dotnet run --project LoadTest [total=1000] [concurrency=20] [baseUrl=https://localhost:5001] [endpoint=/api/messages]

var totalMessages = GetIntArg("totalMessages", 1000);
var concurrency = GetIntArg("concurrency", 20);
var baseUrl = GetStringArg("baseUrl", "https://localhost:5001");
var endpoint = GetStringArg("endpoint", "/api/messages"); // adjust if your API uses different route
var maxRetries = GetIntArg("maxRetries", 2);

Console.WriteLine($"Load Test: total={totalMessages}, concurrency={concurrency}, baseUrl={baseUrl}, endpoint={endpoint}, maxRetries={maxRetries}");

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
using var cts = new CancellationTokenSource();
var ct = cts.Token;

var rng = new Random();
var semaphore = new SemaphoreSlim(concurrency);
var sw = Stopwatch.StartNew();
int success = 0, failed = 0;

var tasks = new Task[totalMessages];
for (int i = 0; i < totalMessages; i++)
{
    await semaphore.WaitAsync(ct);
    var idx = i;
    tasks[i] = Task.Run(async () =>
    {
        try
        {
            var tenant = $"tenant{rng.Next(1, 11)}";
            var channel = rng.NextDouble() < 0.5 ? "email" : "whatsapp";
            var to = channel == "email" ? RandomEmail(rng) : RandomPhone(rng);
            var templates = new[] { "WELCOME", "REMINDER", "ALERT" };
            var template = templates[rng.Next(templates.Length)];

            var payload = new
            {
                TenantId = tenant,
                Channel = channel,
                To = to,
                TemplateCode = template,
                Data = new { sample = "data", idx }
            };

            var attempts = 0;
            var successLocal = false;
            while (attempts <= maxRetries && !successLocal)
            {
                attempts++;
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                    };

                    var res = await http.SendAsync(req, ct);
                    if (res.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref success);
                        successLocal = true;
                    }
                    else
                    {
                        // non-200
                        Console.WriteLine($"[{idx}] Failed status {res.StatusCode} after attempt {attempts}");
                        if (attempts > maxRetries) Interlocked.Increment(ref failed);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref failed);
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{idx}] Exception on attempt {attempts}: {ex.Message}");
                    if (attempts > maxRetries) Interlocked.Increment(ref failed);
                    await Task.Delay(100);
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }, ct);
}

await Task.WhenAll(tasks);
sw.Stop();

var total = success + failed;
var seconds = Math.Max(1, (int)sw.Elapsed.TotalSeconds);
Console.WriteLine("\n--- Summary ---");
Console.WriteLine($"Total: {total}");
Console.WriteLine($"Success: {success}");
Console.WriteLine($"Failed: {failed}");
Console.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"Throughput: {total / sw.Elapsed.TotalSeconds:F2} req/sec");

// helpers
static int GetIntArg(string name, int @default)
{
    foreach (var a in Environment.GetCommandLineArgs())
    {
        if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            var v = a.Substring(name.Length + 1);
            if (int.TryParse(v, out var r)) return r;
        }
    }
    return @default;
}

static string GetStringArg(string name, string @default)
{
    foreach (var a in Environment.GetCommandLineArgs())
    {
        if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            return a.Substring(name.Length + 1);
        }
    }
    return @default;
}

static string RandomEmail(Random rng)
{
    var names = new[] { "alice", "bob", "carol", "dave", "eve", "frank", "grace" };
    var domains = new[] { "example.com", "test.local", "mail.com" };
    return $"{names[rng.Next(names.Length)]}{rng.Next(1,1000)}@{domains[rng.Next(domains.Length)]}";
}

static string RandomPhone(Random rng)
{
    // simple E.164-like numbers
    return $"+62{rng.Next(800000000, 999999999)}";
}
