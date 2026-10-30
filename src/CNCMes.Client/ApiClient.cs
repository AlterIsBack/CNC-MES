using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CNCMes.Client;

public class ClientConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5000";
    public string LastUsername { get; set; } = "";
    public string LastMachineId { get; set; } = "R01";
    public List<string> SelectedMachines { get; set; } = new() { "R01", "R02" };
    public Dictionary<string, List<string>> CustomQuickButtons { get; set; } = new();
}

public class UserSession
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string MachineId { get; set; } = "R01";
    public List<string> SelectedMachines { get; set; } = new() { "R01", "R02" };
}

public class ApiClient
{
    // Closed-network/Offline LAN optimization: bypass Windows WPAD auto-proxy queries!
    private static readonly HttpClientHandler _handler = new()
    {
        UseProxy = false,
        Proxy = null,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    };

    private readonly HttpClient _http = new(_handler) { Timeout = TimeSpan.FromSeconds(4) };
    private readonly string _configFile;

    public ClientConfig Config { get; private set; } = new();

    public ApiClient()
    {
        _configFile = Path.Combine(AppContext.BaseDirectory, "client_config.json");
        LoadConfig();
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFile))
            {
                var json = File.ReadAllText(_configFile);
                Config = JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
                Config.ServerUrl = NormalizeServerUrl(Config.ServerUrl);
            }
        }
        catch { }
    }

    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFile, json);
        }
        catch { }
    }

    /// <summary>
    /// Normalizes user input such as "192.168.0.10", "\\PC-NAME\share", "http://192.168.0.10" into "http://host:5000"
    /// </summary>
    public static string NormalizeServerUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "http://localhost:5000";
        var s = raw.Trim().TrimEnd('/');

        // Handle UNC shared folder path like \\192.168.0.10\share or \\DESKTOP-ABC
        if (s.StartsWith(@"\\"))
        {
            s = s.Substring(2);
            var slashIdx = s.IndexOfAny(new[] { '\\', '/' });
            if (slashIdx > 0) s = s.Substring(0, slashIdx);
        }

        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            s = "http://" + s;
        }

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            // If user didn't specify a port (default port 80), automatically use port 5000
            int port = uri.IsDefaultPort ? 5000 : uri.Port;
            return $"{uri.Scheme}://{uri.Host}:{port}";
        }

        return s;
    }

    public void SetServerUrl(string url)
    {
        Config.ServerUrl = NormalizeServerUrl(url);
        SaveConfig();
    }

    public static List<(IPAddress Ip, IPAddress Mask)> GetActiveLocalInterfaces()
    {
        var list = new List<(IPAddress Ip, IPAddress Mask)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var props = ni.GetIPProperties();
                foreach (var unicast in props.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(unicast.Address))
                    {
                        var mask = unicast.IPv4Mask ?? IPAddress.Parse("255.255.255.0");
                        list.Add((unicast.Address, mask));
                    }
                }
            }
        }
        catch { }
        return list;
    }

    public static List<string> GetLocalIPv4List()
    {
        var list = GetActiveLocalInterfaces().Select(x => x.Ip.ToString()).Distinct().ToList();
        if (list.Count == 0)
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        list.Add(ip.ToString());
                    }
                }
            }
            catch { }
        }
        return list.Distinct().ToList();
    }

    public async Task<bool> QuickProbeServerAsync(string candidateUrl, int timeoutMs = 600)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var res = await _http.GetAsync($"{candidateUrl.TrimEnd('/')}/api/auth/status", cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 4-Stage Offline LAN Server Discovery:
    /// 1) Shared Folder / server_info.json check
    /// 2) Windows ARP Cache (arp -a) active peer instant scan (finds shared-folder/pinged Server PC in 0.1s!)
    /// 3) Per-NIC bound UDP Subnet Broadcast (works without default gateway on offline switch!)
    /// 4) Parallel /24 Subnet HTTP scan
    /// </summary>
    public async Task<string?> DiscoverServerAsync(Action<string>? progressCallback = null)
    {
        // Stage 1: Check adjacent server_info.json or UNC path
        progressCallback?.Invoke("1단계: 공유폴더 및 서버 정보 파일 확인 중...");
        try
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (baseDir.StartsWith(@"\\"))
            {
                var uncCandidate = NormalizeServerUrl(baseDir);
                if (await QuickProbeServerAsync(uncCandidate, 800)) return uncCandidate;
            }

            var parentDir = Directory.GetParent(baseDir)?.FullName;
            if (!string.IsNullOrEmpty(parentDir))
            {
                string[] infoPaths =
                {
                    Path.Combine(parentDir, "server_info.json"),
                    Path.Combine(parentDir, "1_중앙_서버_실행", "server_info.json")
                };
                foreach (var p in infoPaths)
                {
                    if (File.Exists(p))
                    {
                        var doc = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(p));
                        if (doc.TryGetProperty("AllUrls", out var urls) && urls.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var u in urls.EnumerateArray())
                            {
                                var urlStr = u.GetString();
                                if (!string.IsNullOrEmpty(urlStr) && await QuickProbeServerAsync(urlStr, 700))
                                    return urlStr;
                            }
                        }
                        if (doc.TryGetProperty("HostUrl", out var hostUrl))
                        {
                            var hStr = hostUrl.GetString();
                            if (!string.IsNullOrEmpty(hStr) && await QuickProbeServerAsync(hStr, 700))
                                return hStr;
                        }
                    }
                }
            }
        }
        catch { }

        // Stage 2: ARP Table (arp -a) Instant Neighbor Scan (Finds any PC connected via Shared Folder or Ping in 0.1s!)
        progressCallback?.Invoke("2단계: 공유폴더/핑 연결된 사내 PC 목록(ARP) 즉시 검사 중...");
        try
        {
            var arpIps = GetArpCacheIPv4Addresses();
            if (arpIps.Count > 0)
            {
                string? arpFound = null;
                var arpTasks = arpIps.Select(async ip =>
                {
                    var candidate = $"http://{ip}:5000";
                    if (await QuickProbeServerAsync(candidate, 800))
                    {
                        arpFound = candidate;
                    }
                });
                await Task.WhenAll(arpTasks);
                if (!string.IsNullOrEmpty(arpFound)) return arpFound;
            }
        }
        catch { }

        // Stage 3: Per-NIC Bound UDP Broadcast (Works on offline switches with NO default gateway!)
        progressCallback?.Invoke("3단계: 모든 랜카드(NIC) UDP 브로드캐스트 탐색 중...");
        try
        {
            var nicList = GetActiveLocalInterfaces();
            var pingBytes = Encoding.UTF8.GetBytes("CNCMES_DISCOVERY_PING");

            var udpTasks = nicList.Select(async nic =>
            {
                try
                {
                    using var udp = new UdpClient(new IPEndPoint(nic.Ip, 0));
                    udp.EnableBroadcast = true;

                    // Send to Subnet-Directed Broadcast (e.g. 192.168.0.255 or 169.254.255.255) AND 255.255.255.255
                    var subnetBroadcast = GetBroadcastAddress(nic.Ip, nic.Mask);
                    await udp.SendAsync(pingBytes, pingBytes.Length, new IPEndPoint(subnetBroadcast, 5002));
                    try { await udp.SendAsync(pingBytes, pingBytes.Length, new IPEndPoint(IPAddress.Broadcast, 5002)); } catch { }

                    using var cts = new CancellationTokenSource(900);
                    var res = await udp.ReceiveAsync(cts.Token);
                    var reply = Encoding.UTF8.GetString(res.Buffer);
                    if (reply.StartsWith("CNCMES_SERVER_ACK|"))
                    {
                        // Use actual packet source IP (res.RemoteEndPoint.Address) first — guaranteed reachable!
                        var senderIp = res.RemoteEndPoint.Address.ToString();
                        var directUrl = $"http://{senderIp}:5000";
                        if (await QuickProbeServerAsync(directUrl, 700))
                            return directUrl;

                        var parts = reply.Split('|');
                        if (parts.Length >= 2 && await QuickProbeServerAsync(parts[1].Trim(), 700))
                            return parts[1].Trim();
                    }
                }
                catch { }
                return (string?)null;
            }).ToList();

            var udpResults = await Task.WhenAll(udpTasks);
            var validUdp = udpResults.FirstOrDefault(r => !string.IsNullOrEmpty(r));
            if (!string.IsNullOrEmpty(validUdp)) return validUdp;
        }
        catch { }

        // Stage 4: Fast Parallel HTTP Subnet Scan across all active NIC subnets
        var localIps = GetLocalIPv4List();
        var subnets = localIps.Select(ip =>
        {
            var parts = ip.Split('.');
            return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : "";
        }).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

        if (!subnets.Contains("192.168.0")) subnets.Add("192.168.0");
        if (!subnets.Contains("192.168.1")) subnets.Add("192.168.1");

        using var scanHttp = new HttpClient(_handler) { Timeout = TimeSpan.FromMilliseconds(600) };

        foreach (var subnet in subnets)
        {
            progressCallback?.Invoke($"4단계: 사내망 대역 [{subnet}.1 ~ 254] 고속 스캔 중...");

            using var foundCts = new CancellationTokenSource();
            string? foundUrl = null;

            var tasks = Enumerable.Range(1, 254).Select(async i =>
            {
                if (foundCts.IsCancellationRequested) return;
                var candidateUrl = $"http://{subnet}.{i}:5000";
                try
                {
                    var res = await scanHttp.GetAsync($"{candidateUrl}/api/auth/status", foundCts.Token);
                    if (res.IsSuccessStatusCode)
                    {
                        foundUrl = candidateUrl;
                        foundCts.Cancel();
                    }
                }
                catch { }
            }).ToList();

            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(2200));
            if (!string.IsNullOrEmpty(foundUrl))
            {
                return foundUrl;
            }
        }

        return null;
    }

    private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        byte[] ipAdressBytes = address.GetAddressBytes();
        byte[] subnetMaskBytes = subnetMask.GetAddressBytes();
        byte[] broadcastAddress = new byte[ipAdressBytes.Length];
        for (int i = 0; i < broadcastAddress.Length; i++)
        {
            broadcastAddress[i] = (byte)(ipAdressBytes[i] | (subnetMaskBytes[i] ^ 255));
        }
        return new IPAddress(broadcastAddress);
    }

    private static List<string> GetArpCacheIPv4Addresses()
    {
        var list = new List<string>();
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1000);
                var matches = Regex.Matches(output, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
                foreach (Match m in matches)
                {
                    var ip = m.Value;
                    if (!ip.StartsWith("224.") && !ip.StartsWith("239.") && !ip.EndsWith(".255") && ip != "255.255.255.255" && ip != "127.0.0.1")
                    {
                        list.Add(ip);
                    }
                }
            }
        }
        catch { }
        return list.Distinct().ToList();
    }

    public async Task<(bool ok, string message)> TestConnectionAsync()
    {
        try
        {
            var res = await _http.GetAsync($"{Config.ServerUrl}/api/auth/status");
            if (res.IsSuccessStatusCode)
            {
                return (true, "연결 성공 (서버 정상 작동 중)");
            }
            return (false, $"서버 오류 응답 (HTTP {(int)res.StatusCode})");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"연결 거부 ({Config.ServerUrl}): {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, $"연결 시간 초과 ({Config.ServerUrl}) - 서버 PC 방화벽 또는 IP 확인");
        }
        catch (Exception ex)
        {
            return (false, $"연결 실패: {ex.Message}");
        }
    }

    public async Task<(bool ok, string msg, UserSession? session)> LoginAsync(string username, string password, string machineId = "R01")
    {
        try
        {
            var body = new { Username = username, Password = password };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/auth/login", body);
            if (!res.IsSuccessStatusCode)
            {
                var errObj = await res.Content.ReadFromJsonAsync<JsonElement>();
                var msg = errObj.TryGetProperty("message", out var m) ? m.GetString() : "로그인 실패";
                return (false, msg ?? "로그인에 실패했습니다.", null);
            }

            var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
            var savedMachines = Config.SelectedMachines?.Count > 0 ? Config.SelectedMachines : new List<string> { "R01", "R02" };
            var session = new UserSession
            {
                Username = doc.GetProperty("username").GetString() ?? username,
                DisplayName = doc.GetProperty("displayName").GetString() ?? username,
                Role = doc.GetProperty("role").GetString() ?? "WORKER",
                MachineId = savedMachines[0],
                SelectedMachines = new List<string>(savedMachines)
            };

            Config.LastUsername = username;
            SaveConfig();

            return (true, "로그인 성공", session);
        }
        catch (Exception ex)
        {
            return (false, $"서버 통신 오류 ({Config.ServerUrl}): {ex.Message}", null);
        }
    }

    public async Task<(bool ok, string msg)> RegisterAsync(string username, string password, string displayName)
    {
        try
        {
            var body = new { Username = username, Password = password, DisplayName = displayName, Role = "WORKER" };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/auth/register", body);
            if (res.IsSuccessStatusCode)
            {
                return (true, "작업자 계정이 등록되었습니다. 로그인해 주십시오.");
            }
            var errObj = await res.Content.ReadFromJsonAsync<JsonElement>();
            var msg = errObj.TryGetProperty("message", out var m) ? m.GetString() : "등록 실패";
            return (false, msg ?? "등록에 실패했습니다.");
        }
        catch (Exception ex)
        {
            return (false, $"통신 오류 ({Config.ServerUrl}): {ex.Message}");
        }
    }

    public async Task<List<JsonElement>> GetWorkOrdersAsync(bool includeHidden = true)
    {
        try
        {
            var url = includeHidden ? $"{Config.ServerUrl}/api/workorders?includeHidden=true" : $"{Config.ServerUrl}/api/workorders";
            var res = await _http.GetFromJsonAsync<JsonElement>(url);
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<(bool success, string message)> HideWorkOrderAsync(int orderId)
    {
        try
        {
            var res = await _http.PostAsync($"{Config.ServerUrl}/api/workorders/{orderId}/hide", null);
            if (res.IsSuccessStatusCode) return (true, "목록에서 숨김 처리되었습니다.");
            return (false, $"숨김 처리 실패: {res.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"통신 오류: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> UnhideWorkOrderAsync(int orderId)
    {
        try
        {
            var res = await _http.PostAsync($"{Config.ServerUrl}/api/workorders/{orderId}/unhide", null);
            if (res.IsSuccessStatusCode) return (true, "목록에 다시 표시되도록 복원되었습니다.");
            return (false, $"표시 복원 실패: {res.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"통신 오류: {ex.Message}");
        }
    }

    public async Task<(bool success, string message, int syncedCount)> SyncShareFolderAsync()
    {
        try
        {
            var res = await _http.PostAsync($"{Config.ServerUrl}/api/workorders/share-folder/sync", null);
            var content = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                int count = root.TryGetProperty("syncedCount", out var c) ? c.GetInt32() : 0;
                string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "동기화 완료" : "동기화 완료";
                return (true, msg, count);
            }
            return (false, $"동기화 실패: {res.StatusCode} - {content}", 0);
        }
        catch (Exception ex)
        {
            return (false, $"동기화 통신 오류: {ex.Message}", 0);
        }
    }

    public async Task<(bool success, string message)> CompleteWorkOrderAsync(int orderId, string worker, string? notes = null)
    {
        try
        {
            var payload = new { completedBy = worker, notes = notes };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/workorders/{orderId}/complete", payload);
            var content = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "가공완료 및 백업보관 처리 성공" : "가공완료 및 백업보관 처리 성공";
                return (true, msg);
            }
            return (false, $"완료 처리 실패: {content}");
        }
        catch (Exception ex)
        {
            return (false, $"완료 처리 통신 오류: {ex.Message}");
        }
    }

    public async Task<JsonElement?> GetOrderFilesAsync(int orderId)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/workorders/{orderId}/files");
            return res;
        }
        catch { }
        return null;
    }

    public async Task<JsonElement?> GetPartHistoryAsync(string partCode)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/permanentevents/history/{Uri.EscapeDataString(partCode)}");
            return res;
        }
        catch { }
        return null;
    }

    public async Task<List<JsonElement>> GetExecutionsAsync(int stepId)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/executions/step/{stepId}");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<int> StartExecutionAsync(int stepId, string machineId, string workerName)
    {
        var body = new
        {
            StepId = stepId,
            AssignmentId = (int?)null,
            MachineId = machineId,
            WorkerUsername = workerName,
            TargetQty = 1,
            Notes = "현장 단말기 가공 시작"
        };
        var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/executions/start", body);
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("executionId").GetInt32();
    }

    public async Task FinishExecutionAsync(int executionId, int machinedQty)
    {
        var body = new
        {
            ExecutionId = executionId,
            MachinedQty = machinedQty,
            Notes = "현장 단말기 가공 완료 보고"
        };
        var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/executions/finish", body);
        res.EnsureSuccessStatusCode();
    }

    public async Task CreatePermanentEventAsync(string partCode, string machineId, string eventType, string desc, string rootCause, string action, string workerName)
    {
        var body = new
        {
            PartCode = partCode,
            MachineId = machineId,
            EventCategory = "가공중",
            EventType = eventType,
            Description = desc,
            Cause = rootCause,
            ActionTaken = action,
            AuthorUsername = workerName
        };
        var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/permanentevents", body);
        res.EnsureSuccessStatusCode();
    }

    public async Task<List<JsonElement>> GetChatMessagesAsync(string channel)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/communications/chat?channelType=MACHINE&topicKey={Uri.EscapeDataString(channel)}");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<List<JsonElement>> GetMachinesAsync()
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/machines");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<bool> UpdateMachineStatusAsync(string machineId, string status, string? jobNum, string? partName, string? programOrNotes, string workerName)
    {
        try
        {
            var body = new
            {
                Status = status,
                CurrentProgram = programOrNotes,
                SpindleRpm = status == "가공중" ? 8000 : 0,
                FeedRate = status == "가공중" ? 1500.0 : 0.0,
                ActiveJobNumber = jobNum,
                ActivePartName = partName,
                ActiveWorker = workerName
            };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/machines/{machineId}/status", body);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task SendChatMessageAsync(string channel, string sender, string message, string channelType = "MACHINE")
    {
        try
        {
            var body = new
            {
                ChannelType = channelType,
                TopicKey = channel,
                Author = sender,
                Content = message
            };
            await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/communications/chat", body);
        }
        catch { }
    }

    public async Task<List<JsonElement>> GetChatMessagesByChannelAsync(string channelType, string topicKey)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/communications/chat?channelType={Uri.EscapeDataString(channelType)}&topicKey={Uri.EscapeDataString(topicKey)}");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<List<JsonElement>> GetMachineWorkLogsAsync(string? machineId = null, int limit = 60)
    {
        try
        {
            var q = string.IsNullOrWhiteSpace(machineId) ? $"?limit={limit}" : $"?machineId={Uri.EscapeDataString(machineId)}&limit={limit}";
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/machines/work-logs{q}");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<bool> AddManualWorkLogAsync(string machineId, string orderNumber, string partName, string workDetail, string status, string workerName, string? subWorkerName = null, int qty = 0)
    {
        try
        {
            var body = new
            {
                MachineId = machineId,
                OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? "-" : orderNumber,
                PartName = string.IsNullOrWhiteSpace(partName) ? "-" : partName,
                WorkDetail = workDetail,
                Status = status,
                WorkerName = workerName,
                SubWorkerName = subWorkerName,
                Qty = qty
            };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/machines/manual-log", body);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<JsonElement>> GetSubWorkerTasksAsync(string? leader = null)
    {
        try
        {
            var q = string.IsNullOrWhiteSpace(leader) ? "" : $"?leader={Uri.EscapeDataString(leader)}";
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/machines/subtasks{q}");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<bool> CreateSubWorkerTaskAsync(string machineId, string orderNumber, string partName, string leaderUsername, string subWorkerName, string instruction)
    {
        try
        {
            var body = new
            {
                MachineId = machineId,
                OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? "-" : orderNumber,
                PartName = string.IsNullOrWhiteSpace(partName) ? "-" : partName,
                LeaderUsername = leaderUsername,
                SubWorkerName = subWorkerName,
                Instruction = instruction
            };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/machines/subtasks", body);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ReportSubWorkerTaskAsync(int taskId, string reportContent, int reportedQty, string machineStatus = "가공중")
    {
        try
        {
            var body = new
            {
                ReportContent = reportContent,
                ReportedQty = reportedQty,
                MachineStatus = machineStatus
            };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/machines/subtasks/{taskId}/report", body);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<JsonElement>> GetNoticesAsync()
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/communications/notices");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<bool> AcknowledgeNoticeAsync(int noticeId, string username)
    {
        try
        {
            var res = await _http.PostAsync($"{Config.ServerUrl}/api/communications/notices/{noticeId}/ack?username={Uri.EscapeDataString(username)}", null);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateMachineWorkLogAsync(int id, string machineId, string orderNumber, string partName, string workDetail, string status, string workerName, int qty, string modifiedBy)
    {
        try
        {
            var body = new
            {
                Id = id,
                MachineId = machineId,
                OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? "-" : orderNumber,
                PartName = string.IsNullOrWhiteSpace(partName) ? "-" : partName,
                WorkDetail = workDetail,
                Status = status,
                WorkerName = workerName,
                Qty = qty
            };
            var res = await _http.PutAsJsonAsync($"{Config.ServerUrl}/api/machines/work-logs/{id}?modifiedBy={Uri.EscapeDataString(modifiedBy)}", body);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteMachineWorkLogAsync(int id, string deletedBy)
    {
        try
        {
            var res = await _http.DeleteAsync($"{Config.ServerUrl}/api/machines/work-logs/{id}?deletedBy={Uri.EscapeDataString(deletedBy)}");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<JsonElement>> GetAllPermanentEventsAsync()
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/permanentevents");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<bool> CreateDefectAndCountermeasureAsync(string partCode, string machineId, string category, string eventType, string description, string cause, string actionTaken, string author)
    {
        try
        {
            var body = new
            {
                PartCode = string.IsNullOrWhiteSpace(partCode) ? "-" : partCode,
                MachineId = machineId,
                EventCategory = category,
                EventType = eventType,
                Description = description,
                Cause = cause,
                ActionTaken = actionTaken,
                AuthorUsername = author,
                Status = "진행"
            };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/permanentevents", body);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateDefectAndCountermeasureAsync(int eventId, string partCode, string machineId, string category, string eventType, string description, string cause, string actionTaken, string author, string status = "진행")
    {
        try
        {
            var body = new
            {
                EventId = eventId,
                PartCode = string.IsNullOrWhiteSpace(partCode) ? "-" : partCode,
                MachineId = machineId,
                EventCategory = category,
                EventType = eventType,
                Description = description,
                Cause = cause,
                ActionTaken = actionTaken,
                AuthorUsername = author,
                Status = status
            };
            var res = await _http.PutAsJsonAsync($"{Config.ServerUrl}/api/permanentevents/{eventId}", body);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // =========================================================================
    // 사용 공구 이력 API (주요 가공 공구 / 메이커 / 규격 / 날장 / 재질 / 기타)
    // =========================================================================
    public async Task<List<JsonElement>> GetUsedToolsAsync(string? machineId = null)
    {
        try
        {
            var q = string.IsNullOrWhiteSpace(machineId) ? "" : $"?machineId={Uri.EscapeDataString(machineId)}";
            var res = await _http.GetFromJsonAsync<JsonElement>($"{Config.ServerUrl}/api/machines/used-tools{q}");
            if (res.ValueKind == JsonValueKind.Array)
            {
                return res.EnumerateArray().ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<bool> AddUsedToolAsync(string machineId, string orderNumber, string partName, string toolName, string maker, string diameter, string fluteLength, string material, string notes, string workerName)
    {
        try
        {
            var body = new
            {
                MachineId = machineId,
                OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? "-" : orderNumber,
                PartName = string.IsNullOrWhiteSpace(partName) ? "-" : partName,
                ToolName = toolName,
                Maker = string.IsNullOrWhiteSpace(maker) ? "-" : maker,
                ToolDiameter = string.IsNullOrWhiteSpace(diameter) ? "-" : diameter,
                FluteLength = string.IsNullOrWhiteSpace(fluteLength) ? "-" : fluteLength,
                Material = string.IsNullOrWhiteSpace(material) ? "-" : material,
                Notes = string.IsNullOrWhiteSpace(notes) ? "-" : notes,
                WorkerName = workerName
            };
            var res = await _http.PostAsJsonAsync($"{Config.ServerUrl}/api/machines/used-tools", body);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteUsedToolAsync(int toolLogId, string workerName)
    {
        try
        {
            var res = await _http.DeleteAsync($"{Config.ServerUrl}/api/machines/used-tools/{toolLogId}?deletedBy={Uri.EscapeDataString(workerName)}");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // =========================================================================
    // 카테고리 탭별 12개 빠른 작업이력 버튼 프리셋 관리 (작업자 커스텀 편집 지원)
    // =========================================================================
    public static List<string> GetDefaultQuickButtonsForCategory(string statusKey)
    {
        return statusKey switch
        {
            "세팅중" => new List<string>
            {
                "🔧 소재 장착 및 바이스/치구 세팅",
                "🔧 G54 공작물 원점(X/Y/Z) 세팅",
                "🔧 공구 장착 및 공구길이(H) 세팅",
                "🔧 NC 프로그램 전송 및 경로 검증",
                "🔧 소프트죠(Jaws) 성형 및 평행 맞춤",
                "🔧 초품(첫물) 시가공 및 세팅 확인",
                "🔧 도면/공정도 및 세팅시트 검토",
                "🔧 치구 클램프 압력 및 위치 조정",
                "🔧 공구경(D) 옵셋 초기 보정 입력",
                "🔧 절삭유 노즐 각도/에어블로우 세팅",
                "🔧 단도리 교체 (이전 지그 탈거)",
                "🔧 세팅 완료 후 본가공 진입 대기"
            },
            "가공중" => new List<string>
            {
                "⚡ 1차 전면 황삭 가공 진행",
                "⚡ 1차 전면 중삭/정삭 가공 진행",
                "⚡ 2차 후면(반전) 황삭 가공 진행",
                "⚡ 2차 후면(반전) 정삭 가공 진행",
                "⚡ 마이크로 홀 드릴링 정밀 가공",
                "⚡ 포켓 및 내경 윤곽 밀링 가공",
                "⚡ 외곽 윤곽 및 측면 정밀 가공",
                "⚡ 챔퍼(면취) 및 버(Burr) 제거 가공",
                "⚡ 탭(Tap) / 나사산 가공 진행",
                "⚡ 고속 정밀 피니싱 가공 진행",
                "⚡ 치수 보정 후 재가공(수정가공) 진행",
                "⚡ 양산 연속 자동가공 정상 가동중"
            },
            "측정중" => new List<string>
            {
                "🔬 초품(첫물) 전수 치수 측정 중",
                "🔬 1차 가공 후 버니어/마이크로 측정",
                "🔬 정삭 후 포켓 깊이/두께 측정",
                "🔬 마이크로 홀 내경 핀게이지 검사",
                "🔬 외경/전장 주요 공차 적합성 확인",
                "🔬 평탄도 및 직각도 다이얼게이지 측정",
                "🔬 표면 조도 및 가공면 육안 검사",
                "🔬 측정실(Micro-Vu/3차원) 정밀측정 의뢰",
                "🔬 옵셋 보정 후 재측정 확인 중",
                "🔬 중간 샘플링 치수 검사 (주기 측정)",
                "🔬 최종 가공완료 후 출하 전 치수 확인",
                "🔬 측정 데이터 기록 및 합격 판정 확인"
            },
            "공구교체" => new List<string>
            {
                "🛠️ 황삭 엔드밀 마모 교체 및 세팅",
                "🛠️ 정삭 엔드밀 수명 도달 신품 교체",
                "🛠️ 마이크로 드릴 교체 및 런아웃 점검",
                "🛠️ 챔퍼밀/면취 공구 신품 교체",
                "🛠️ 탭(Tap) 공구 교체 및 토크 확인",
                "🛠️ 인서트 팁(Insert) 코너 회전/교체",
                "🛠️ 공구 교체 후 Z축 길이 옵셋 재보정",
                "🛠️ 공구 교체 후 공구경(D) 마모량 보정",
                "🛠️ 콜렛 척/홀더 청소 및 밸런스 점검",
                "🛠️ 매거진 공구 번호 배열 재정리",
                "🛠️ 특수 공구(성형툴) 장착 및 테스트",
                "🛠️ 파손 공구 잔여물 제거 및 신품 장착"
            },
            "대기" => new List<string>
            {
                "⏸️ 장비 내부 칩(Chip) 청소 및 정리",
                "⏸️ 절삭유(쿨런트) 농도 점검 및 보충",
                "⏸️ 소재 반입 및 다음 작지 투입 대기",
                "⏸️ 측정실 초품/중간검사 판정 결과 대기",
                "⏸️ 도면/NC프로그램 수정 확인 대기",
                "⏸️ 전공정 완료 부품 인계 대기",
                "⏸️ 가공 완료품 에어건 세척 및 적재",
                "⏸️ 일일 설비 예방점검 (윤활유/공압)",
                "⏸️ 교대 시간 작업 인수인계 준비",
                "⏸️ 치구/자재 수급 대기 중",
                "⏸️ 서브인원 작업 교육 및 업무 지시",
                "⏸️ 작업자 휴식 / 중식 일시대기"
            },
            "알람" => new List<string>
            {
                "🚨 공구 파손 발생 - 즉시 정지 및 확인",
                "🚨 치수 공차 이탈(불량) - 원인 파악중",
                "🚨 서보/스핀들 과부하 알람 확인 중",
                "🚨 공구교환장치(ATC) 매거진 알람 조치",
                "🚨 절삭유 펌프/에어 압력 저하 알람",
                "🚨 원점 틀어짐 / 치구 들뜸 현상 확인",
                "🚨 NC 프로그램 좌표/코드 오류 수정 중",
                "🚨 소재 클램핑 불량 / 진동(채터링) 발생",
                "🚨 칩 말림으로 인한 가공면 찍힘 확인",
                "🚨 비상정지(EMG) 후 원점 복귀 조치 중",
                "🚨 설비 보전팀/관리자 점검 요청 대기",
                "🚨 알람 해제 및 시운전 정상 여부 확인"
            },
            _ => new List<string>
            {
                "작업 1", "작업 2", "작업 3", "작업 4",
                "작업 5", "작업 6", "작업 7", "작업 8",
                "작업 9", "작업 10", "작업 11", "작업 12"
            }
        };
    }

    public List<string> GetQuickButtons(string statusKey)
    {
        Config.CustomQuickButtons ??= new Dictionary<string, List<string>>();
        if (!Config.CustomQuickButtons.TryGetValue(statusKey, out var list) || list == null || list.Count < 12)
        {
            var defaults = GetDefaultQuickButtonsForCategory(statusKey);
            if (list != null && list.Count > 0)
            {
                for (int i = 0; i < Math.Min(list.Count, 12); i++)
                {
                    if (!string.IsNullOrWhiteSpace(list[i]))
                        defaults[i] = list[i];
                }
            }
            Config.CustomQuickButtons[statusKey] = new List<string>(defaults);
            SaveConfig();
            return Config.CustomQuickButtons[statusKey];
        }
        return list;
    }

    public void SaveQuickButtonsForCategory(string statusKey, List<string> labels)
    {
        Config.CustomQuickButtons ??= new Dictionary<string, List<string>>();
        var defaults = GetDefaultQuickButtonsForCategory(statusKey);
        var cleanList = new List<string>(12);
        for (int i = 0; i < 12; i++)
        {
            var val = (i < labels.Count && !string.IsNullOrWhiteSpace(labels[i])) ? labels[i].Trim() : defaults[i];
            cleanList.Add(val);
        }
        Config.CustomQuickButtons[statusKey] = cleanList;
        SaveConfig();
    }

    public void ResetQuickButtonsForCategory(string statusKey)
    {
        Config.CustomQuickButtons ??= new Dictionary<string, List<string>>();
        Config.CustomQuickButtons[statusKey] = GetDefaultQuickButtonsForCategory(statusKey);
        SaveConfig();
    }

    // =========================================================================
    // 📦 후공정(핀삽입/각인/후처리 등) 보고 및 추적 API (v1.3.7)
    // =========================================================================
    public async Task<(bool success, string message)> SubmitPostProcessReportAsync(
        int partId, int orderId, string orderNumber, string partName, string machineId, 
        string worker, int completedQty, string selectedProcesses, string? notes)
    {
        try
        {
            var payload = new
            {
                partId = partId,
                orderId = orderId,
                orderNumber = orderNumber,
                partName = partName,
                machineId = machineId,
                worker = worker,
                completedQty = completedQty,
                selectedProcesses = selectedProcesses,
                notes = notes
            };
            var res = await _http.PostAsJsonAsync("/api/workorders/post-process-report", payload);
            if (res.IsSuccessStatusCode)
            {
                return (true, "가공 완료 및 후공정 요청이 성공적으로 등록되었습니다.");
            }
            var err = await res.Content.ReadAsStringAsync();
            return (false, $"서버 응답 오류: {err}");
        }
        catch (Exception ex)
        {
            return (false, $"통신 오류: {ex.Message}");
        }
    }

    public async Task<List<JsonElement>> GetPostProcessReportsAsync(string? status = null)
    {
        try
        {
            var url = string.IsNullOrEmpty(status) 
                ? "/api/workorders/post-process-reports" 
                : $"/api/workorders/post-process-reports?status={Uri.EscapeDataString(status)}";
            var res = await _http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return new List<JsonElement>();
            var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
            if (doc != null && doc.RootElement.TryGetProperty("reports", out var reports) && reports.ValueKind == JsonValueKind.Array)
            {
                return reports.EnumerateArray().ToList();
            }
        }
        catch { }
        return new List<JsonElement>();
    }

    public async Task<bool> CompletePostProcessReportAsync(int reportId, string worker)
    {
        try
        {
            var res = await _http.PostAsJsonAsync($"/api/workorders/post-process-reports/{reportId}/complete", new { completedBy = worker });
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
