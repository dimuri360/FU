using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EinstellungenApp
{
    public class CrawlerOptions
    {
        public required string DomainCsv { get; init; }
        public required string ProxyCsv { get; init; }
        public int Rounds { get; init; } = 5;
        public int MaxParallel { get; init; } = 80;
        public int MinDelayMs { get; init; } = 100;
        public int MaxDelayMs { get; init; } = 3000;
        public int FlushIntervalSec { get; init; } = 60;
    }

    public class Crawler
    {
        private readonly ConcurrentDictionary<string, int> _domCnt = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _proxies = new();
        private readonly ConcurrentDictionary<string, int> _hostDelay = new();
        private readonly ConcurrentDictionary<string, byte> _uniqLinks = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _processed = new(StringComparer.OrdinalIgnoreCase);
        private int _linksFound;
        private int _proxiesFound;

        private HttpClient _http = null!;
        private Regex _rxProxy = new(@"\b(?:(?:\d{1,3}\.){3}\d{1,3}):\d{2,5}\b", RegexOptions.Compiled);
        private Regex _rxLink = new(@"https?://[^\s'""<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private SemaphoreSlim? _gate;
        private readonly object _csvLock = new(), _proxyLock = new();
        private int _lastPercent;
        private CrawlerOptions _options = null!;
        private Timer? _timer;
        private readonly Action<string> _log;

        public Crawler(Action<string> log)
        {
            _log = log;
        }

        public async Task RunAsync(CrawlerOptions options, CancellationToken token = default)
        {
            _options = options;
            _linksFound = 0;
            _proxiesFound = 0;
            _gate = new SemaphoreSlim(options.MaxParallel);
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 512,
                AutomaticDecompression = DecompressionMethods.All,
                EnableMultipleHttp2Connections = true
            };
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8),
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("PCrawler/Percent");
            _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

            LoadCSV(options.DomainCsv, l =>
            {
                var p = l.Split(',');
                if (p.Length == 2 && int.TryParse(p[1], out var n))
                    _domCnt[p[0]] = n;
            });

            LoadCSV(options.ProxyCsv, l =>
            {
                var p = l.Split(',');
                if (p.Length >= 2)
                    _proxies[p[0]] = $"{p[1]},{(p.Length > 2 ? p[2] : string.Empty)}";
            });

            _timer = new Timer(_ => FlushAll(), null,
                TimeSpan.FromSeconds(options.FlushIntervalSec),
                TimeSpan.FromSeconds(options.FlushIntervalSec));

            var queue = new ConcurrentQueue<string>(_domCnt.Keys.Select(d => "https://" + d));
            if (queue.IsEmpty)
            {
                const string seed = "https://news.ycombinator.com";
                queue.Enqueue(seed);
                _domCnt.TryAdd(Root(new Uri(seed).Host), 0);
            }

            for (int round = 1; round <= options.Rounds; round++)
            {
                _log($"Round {round}/{options.Rounds}");
                var batch = queue.Distinct()
                                 .OrderBy(u => _domCnt.TryGetValue(Root(new Uri(u).Host), out var c) ? c : 0)
                                 .ToArray();
                queue.Clear();

                _lastPercent = 0;
                int completed = 0;
                int total = batch.Length;

                var tasks = batch.Select(async url =>
                {
                    await CrawlAsync(url, token);
                    int pct = (int)(Interlocked.Increment(ref completed) * 100.0 / total);
                    int prev = Interlocked.Exchange(ref _lastPercent, pct);
                    if (pct > prev)
                        _log($"{pct}% - Links:{_linksFound} - Proxies:{_proxiesFound}");
                }).ToArray();

                await Task.WhenAll(tasks);

                foreach (var d in _domCnt.Keys.Where(k => !_processed.Contains(k)))
                    queue.Enqueue("https://" + d);
            }

            FlushAll();
        }

        private async Task CrawlAsync(string url, CancellationToken token)
        {
            if (_gate == null) return;
            await _gate.WaitAsync(token);
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
                string root = Root(uri.Host);
                if (!_processed.Add(root)) return;

                _domCnt.AddOrUpdate(root, 1, (_, v) => v + 1);

                int d = _hostDelay.GetOrAdd(root, _options.MinDelayMs);
                await Task.Delay(d, token);

                string html;
                try
                {
                    html = await _http.GetStringAsync(uri, token);
                    _hostDelay[root] = _options.MinDelayMs;
                }
                catch
                {
                    _hostDelay[root] = Math.Min(d * 2, _options.MaxDelayMs);
                    return;
                }

                foreach (Match m in _rxProxy.Matches(html))
                {
                    if (_proxies.TryAdd(m.Value, "PENDING"))
                        Interlocked.Increment(ref _proxiesFound);
                }

                foreach (Match m in _rxLink.Matches(html))
                {
                    string link = m.Value;
                    if (!_uniqLinks.TryAdd(link, 0)) continue;
                    Interlocked.Increment(ref _linksFound);
                    if (!Uri.TryCreate(link, UriKind.Absolute, out var luri)) continue;
                    _domCnt.TryAdd(Root(luri.Host), 0);
                }
            }
            finally { _gate.Release(); }
        }

        private void FlushAll()
        {
            lock (_csvLock)
                File.WriteAllLines(_options.DomainCsv,
                    new[] { "domain,call_count" }
                    .Concat(_domCnt.OrderBy(k => k.Key).Select(k => $"{k.Key},{k.Value}")));

            lock (_proxyLock)
                File.WriteAllLines(_options.ProxyCsv,
                    new[] { "proxy,status,latency_ms" }
                    .Concat(_proxies.OrderBy(k => k.Key).Select(k => $"{k.Key},{k.Value}")));

            _log("CSV saved");
        }

        private static void LoadCSV(string path, Action<string> act)
        {
            if (!File.Exists(path)) return;
            foreach (var l in File.ReadLines(path).Skip(1)) act(l);
        }

        private static string Root(string host)
        {
            var p = host.Split('.');
            return p.Length >= 2 ? p[p.Length - 2] + "." + p[p.Length - 1] : host;
        }
    }
}
