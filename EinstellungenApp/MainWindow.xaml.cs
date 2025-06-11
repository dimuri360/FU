using System.Windows;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace EinstellungenApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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
    }
}
