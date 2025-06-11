using System.Windows;
using System.Windows.Threading;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.IO;
using Microsoft.Win32;

namespace EinstellungenApp
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly PerformanceCounter _cpuCounter;
        private readonly NetworkInterface[] _interfaces;
        private long _lastBytesSent;
        private long _lastBytesReceived;
        private Crawler? _crawler;
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // prime
            _interfaces = NetworkInterface.GetAllNetworkInterfaces();
            _lastBytesSent = _interfaces.Sum(i => i.GetIPv4Statistics().BytesSent);
            _lastBytesReceived = _interfaces.Sum(i => i.GetIPv4Statistics().BytesReceived);

            _timer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1) };
            _timer.Tick += UpdateStatus;
            _timer.Start();
        }

        private async void OnScanProxies(object sender, RoutedEventArgs e)
        {
            ProxyOutput.Text = "";
            var lines = ProxyInput.Text.Split('\n');
            foreach (var line in lines)
            {
                var proxy = line.Trim();
                if (string.IsNullOrWhiteSpace(proxy))
                    continue;

                bool success = await CheckProxyAsync(proxy);
                ProxyOutput.AppendText($"{proxy}: {(success ? "OK" : "FAILED")}{System.Environment.NewLine}");
            }
        }

        private async Task<bool> CheckProxyAsync(string proxy)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy(proxy),
                    UseProxy = true
                };
                using var client = new HttpClient(handler) { Timeout = System.TimeSpan.FromSeconds(5) };
                using var response = await client.GetAsync("http://www.google.com");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateStatus(object? sender, System.EventArgs e)
        {
            var cpu = _cpuCounter.NextValue();
            CpuUsageText.Text = $"CPU: {cpu:F0}%";

            long sent = 0;
            long received = 0;
            foreach (var ni in _interfaces)
            {
                var stats = ni.GetIPv4Statistics();
                sent += stats.BytesSent;
                received += stats.BytesReceived;
            }

            var upBytes = sent - _lastBytesSent;
            var downBytes = received - _lastBytesReceived;
            _lastBytesSent = sent;
            _lastBytesReceived = received;

            NetUpText.Text = $"Up: {FormatBytes(upBytes)}/s";
            NetDownText.Text = $"Down: {FormatBytes(downBytes)}/s";
        }

        private static string FormatBytes(long bytes)
        {
            double value = bytes;
            string[] units = { "B", "KB", "MB", "GB" };
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.##} {units[unit]}";
        }

        private void OnBrowseDomainCsv(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
                DomainCsvPath.Text = dlg.FileName;
        }

        private void OnBrowseProxyCsv(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
                ProxyCsvPath.Text = dlg.FileName;
        }

        private async void OnStartCrawler(object sender, RoutedEventArgs e)
        {
            CrawlerLog.Text = string.Empty;
            var opts = new CrawlerOptions
            {
                DomainCsv = DomainCsvPath.Text,
                ProxyCsv = ProxyCsvPath.Text,
                Rounds = int.TryParse(RoundsInput.Text, out var r) ? r : 5,
                MaxParallel = int.TryParse(MaxParallelInput.Text, out var mp) ? mp : 80,
                MinDelayMs = int.TryParse(MinDelayInput.Text, out var mind) ? mind : 100,
                MaxDelayMs = int.TryParse(MaxDelayInput.Text, out var maxd) ? maxd : 3000,
                FlushIntervalSec = int.TryParse(FlushIntervalInput.Text, out var fl) ? fl : 60
            };

            _crawler = new Crawler(msg => Dispatcher.Invoke(() =>
            {
                CrawlerLog.AppendText(msg + System.Environment.NewLine);
                CrawlerLog.ScrollToEnd();
            }));
            _cts = new CancellationTokenSource();
            await Task.Run(() => _crawler.RunAsync(opts, _cts.Token));
        }

        private void OnStopCrawler(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private async void OnRunRssReader(object sender, RoutedEventArgs e)
        {
            RssLog.Text = string.Empty;
            var reader = new RssReader(msg => Dispatcher.Invoke(() =>
            {
                RssLog.AppendText(msg + System.Environment.NewLine);
                RssLog.ScrollToEnd();
            }));
            await reader.RunAsync();
        }
    }
}
