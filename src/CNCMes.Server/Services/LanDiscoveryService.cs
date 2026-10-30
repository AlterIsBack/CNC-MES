using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace CNCMes.Server.Services;

public class LanDiscoveryService : BackgroundService
{
    private const int DiscoveryPort = 5002;
    private readonly ILogger<LanDiscoveryService> _logger;
    private readonly IConfiguration _config;

    public LanDiscoveryService(ILogger<LanDiscoveryService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            udp.EnableBroadcast = true;
            _logger.LogInformation("[LanDiscovery] UDP Broadcast Discovery listener started on port {Port}", DiscoveryPort);

            int serverPort = _config.GetValue<int>("ServerConfig:Port", 5000);

            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                var req = Encoding.UTF8.GetString(result.Buffer);

                if (req.StartsWith("CNCMES_DISCOVERY_PING"))
                {
                    var remoteIp = result.RemoteEndPoint.Address;
                    var bestLocalIp = GetBestMatchingLocalIp(remoteIp);
                    var replyText = $"CNCMES_SERVER_ACK|http://{bestLocalIp}:{serverPort}|{serverPort}";
                    var replyBytes = Encoding.UTF8.GetBytes(replyText);

                    await udp.SendAsync(replyBytes, replyBytes.Length, result.RemoteEndPoint);
                    _logger.LogInformation("[LanDiscovery] Responded to {Remote} with {Reply}", result.RemoteEndPoint, replyText);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning("[LanDiscovery] UDP listener error: {Message}", ex.Message);
        }
        finally
        {
            udp?.Dispose();
        }
    }

    public static string GetBestMatchingLocalIp(IPAddress remoteIp)
    {
        var allIps = GetReliableLocalIpAddresses();
        if (allIps.Count == 0) return "127.0.0.1";

        var remoteBytes = remoteIp.GetAddressBytes();
        if (remoteBytes.Length == 4)
        {
            // Find IP in same /24 subnet first
            foreach (var ipStr in allIps)
            {
                if (IPAddress.TryParse(ipStr, out var localIp))
                {
                    var localBytes = localIp.GetAddressBytes();
                    if (localBytes.Length == 4 &&
                        localBytes[0] == remoteBytes[0] &&
                        localBytes[1] == remoteBytes[1] &&
                        localBytes[2] == remoteBytes[2])
                    {
                        return ipStr;
                    }
                }
            }

            // Find IP in same /16 subnet (e.g. APIPA 169.254.x.x)
            foreach (var ipStr in allIps)
            {
                if (IPAddress.TryParse(ipStr, out var localIp))
                {
                    var localBytes = localIp.GetAddressBytes();
                    if (localBytes.Length == 4 &&
                        localBytes[0] == remoteBytes[0] &&
                        localBytes[1] == remoteBytes[1])
                    {
                        return ipStr;
                    }
                }
            }
        }

        return allIps[0];
    }

    public static List<string> GetReliableLocalIpAddresses()
    {
        var preferred = new List<string>();
        var fallback = new List<string>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                bool isVirtual = desc.Contains("virtual") || desc.Contains("vmware") ||
                                 desc.Contains("vbox") || desc.Contains("wsl") ||
                                 desc.Contains("hyper-v") || desc.Contains("pseudo");

                var props = ni.GetIPProperties();
                foreach (var unicast in props.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(unicast.Address))
                    {
                        var ipStr = unicast.Address.ToString();
                        if (isVirtual || ipStr.StartsWith("169.254."))
                        {
                            fallback.Add(ipStr);
                        }
                        else
                        {
                            preferred.Add(ipStr);
                        }
                    }
                }
            }
        }
        catch { }

        if (preferred.Count > 0)
        {
            preferred.AddRange(fallback);
            return preferred.Distinct().ToList();
        }

        if (fallback.Count > 0)
        {
            return fallback.Distinct().ToList();
        }

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    preferred.Add(ip.ToString());
                }
            }
        }
        catch { }

        return preferred.Distinct().ToList();
    }
}
