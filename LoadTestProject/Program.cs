using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// Simple load tester for the messaging API
// Usage: dotnet run --project LoadTestProject -- totalMessages=1000 concurrency=20 baseUrl=https://localhost:5001 endpoint=/api/messages maxRetries=2

if (HasHelpArg())
{
    PrintUsage();
    return;
}

var totalMessages = GetIntArg(new[] { "totalMessages", "total" }, 1000);
var concurrency = GetIntArg(new[] { "concurrency" }, 20);
var baseUrl = GetStringArg(new[] { "baseUrl" }, "https://localhost:5001");
var endpoint = GetStringArg(new[] { "endpoint" }, "/api/messages");
var maxRetries = GetIntArg(new[] { "maxRetries" }, 2);

Console.WriteLine($"Load Test: total={totalMessages}, concurrency={concurrency}, baseUrl={baseUrl}, endpoint={endpoint}, maxRetries={maxRetries}");

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
using var cts = new CancellationTokenSource();
var ct = cts.Token;

var semaphore = new SemaphoreSlim(concurrency);
var sw = Stopwatch.StartNew();
int success = 0, failed = 0;
int totalAttempts = 0, totalRetries = 0;

var latenciesMs = new ConcurrentBag<double>();
var statusCodeCounts = new ConcurrentDictionary<int, int>();
var attemptsDistribution = new ConcurrentDictionary<int, int>();

var tasks = new Task[totalMessages];
for (int i = 0; i < totalMessages; i++)
{
    await semaphore.WaitAsync(ct);
    var idx = i;
    tasks[i] = Task.Run(async () =>
    {
        var messageSw = Stopwatch.StartNew();
        var attempts = 0;
        try
        {
            var tenant = $"tenant{Random.Shared.Next(1, 11)}";
            var channel = Random.Shared.NextDouble() < 0.5 ? "email" : "whatsapp";
            var to = channel == "email" ? RandomEmail() : RandomPhone();
            var templates = new[] { "WELCOME", "REMINDER", "ALERT" };
            var template = templates[Random.Shared.Next(templates.Length)];

            var payload = new
            {
                TenantId = tenant,
                Channel = channel,
                To = to,
                TemplateCode = template,
                Data = new { sample = "data", idx }
            };

            var successLocal = false;
            while (attempts <= maxRetries && !successLocal)
            {
                attempts++;
                Interlocked.Increment(ref totalAttempts);
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                    };

                    var res = await http.SendAsync(req, ct);
                    statusCodeCounts.AddOrUpdate((int)res.StatusCode, 1, static (_, v) => v + 1);
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

            Interlocked.Add(ref totalRetries, Math.Max(0, attempts - 1));
            attemptsDistribution.AddOrUpdate(attempts, 1, static (_, v) => v + 1);
        }
        finally
        {
            messageSw.Stop();
            latenciesMs.Add(messageSw.Elapsed.TotalMilliseconds);
            semaphore.Release();
        }
    }, ct);
}

await Task.WhenAll(tasks);
sw.Stop();

var total = success + failed;
var latencyValues = latenciesMs.ToArray();
Array.Sort(latencyValues);

Console.WriteLine("\n--- Summary ---");
Console.WriteLine($"Total: {total}");
Console.WriteLine($"Success: {success}");
Console.WriteLine($"Failed: {failed}");
Console.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"Throughput: {total / sw.Elapsed.TotalSeconds:F2} req/sec");

if (latencyValues.Length > 0)
{
    Console.WriteLine("\n--- Latency (ms, per message) ---");
    Console.WriteLine($"Min: {latencyValues[0]:F2}");
    Console.WriteLine($"Avg: {latencyValues.Average():F2}");
    Console.WriteLine($"P50: {Percentile(latencyValues, 0.50):F2}");
    Console.WriteLine($"P95: {Percentile(latencyValues, 0.95):F2}");
    Console.WriteLine($"P99: {Percentile(latencyValues, 0.99):F2}");
    Console.WriteLine($"Max: {latencyValues[^1]:F2}");
}

Console.WriteLine("\n--- HTTP Status Codes ---");
foreach (var kv in statusCodeCounts.OrderBy(k => k.Key))
{
    Console.WriteLine($"{kv.Key}: {kv.Value}");
}

Console.WriteLine("\n--- Retry Stats ---");
Console.WriteLine($"Total attempts: {totalAttempts}");
Console.WriteLine($"Total retries: {totalRetries}");
Console.WriteLine($"Avg attempts/message: {(double)totalAttempts / Math.Max(1, totalMessages):F2}");
foreach (var kv in attemptsDistribution.OrderBy(k => k.Key))
{
    Console.WriteLine($"Attempts={kv.Key}: {kv.Value} message(s)");
}

// helpers
static int GetIntArg(IEnumerable<string> names, int @default)
{
    var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    foreach (var a in Environment.GetCommandLineArgs())
    {
        var idx = a.IndexOf('=');
        if (idx <= 0) continue;
        var key = a[..idx];
        if (nameSet.Contains(key))
        {
            var v = a[(idx + 1)..];
            if (int.TryParse(v, out var r)) return r;
        }
    }
    return @default;
}

static string GetStringArg(IEnumerable<string> names, string @default)
{
    var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    foreach (var a in Environment.GetCommandLineArgs())
    {
        var idx = a.IndexOf('=');
        if (idx <= 0) continue;
        var key = a[..idx];
        if (nameSet.Contains(key))
        {
            return a[(idx + 1)..];
        }
    }
    return @default;
}

static bool HasHelpArg()
{
    var args = Environment.GetCommandLineArgs();
    return args.Any(a => a.Equals("help", StringComparison.OrdinalIgnoreCase)
        || a.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || a.Equals("-h", StringComparison.OrdinalIgnoreCase));
}

static void PrintUsage()
{
    Console.WriteLine("LoadTestProject usage:");
    Console.WriteLine("dotnet run --project LoadTestProject -- totalMessages=1000 concurrency=20 baseUrl=https://localhost:5001 endpoint=/api/messages maxRetries=2");
    Console.WriteLine("Aliases: totalMessages or total");
}

static double Percentile(double[] sortedValues, double percentile)
{
    if (sortedValues.Length == 0) return 0;
    if (sortedValues.Length == 1) return sortedValues[0];

    var rank = percentile * (sortedValues.Length - 1);
    var low = (int)Math.Floor(rank);
    var high = (int)Math.Ceiling(rank);
    if (low == high) return sortedValues[low];

    var weight = rank - low;
    return sortedValues[low] + (sortedValues[high] - sortedValues[low]) * weight;
}

static string RandomEmail()
{
    var names = new[] { "alice", "bob", "carol", "dave", "eve", "frank", "grace" };
    var domains = new[] { "example.com", "test.local", "mail.com" };
    return $"{names[Random.Shared.Next(names.Length)]}{Random.Shared.Next(1, 1000)}@{domains[Random.Shared.Next(domains.Length)]}";
}

static string RandomPhone()
{
    // simple E.164-like numbers
    return $"+62{Random.Shared.Next(800000000, 999999999)}";
}
