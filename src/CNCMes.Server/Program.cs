using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using CNCMes.Server.Data;
using CNCMes.Server.Hubs;
using CNCMes.Server.Models;
using CNCMes.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure port: try 5000, fallback to 5001..5010 if occupied
var configuredPort = builder.Configuration.GetValue<int>("ServerConfig:Port", 5000);
var port = FindAvailablePort(configuredPort);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, port);
});

// Add services
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = false;
    });

builder.Services.AddSignalR();
builder.Services.AddSingleton<SqliteDb>();
builder.Services.AddHostedService<LanDiscoveryService>();

// CORS: allow any origin for local network access
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalNetwork", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SqliteDb>();
    db.Initialize();
}

// Serve static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseCors("AllowLocalNetwork");

app.MapControllers();
app.MapHub<MesHub>("/meshub");

// Fallback to index.html for SPA
app.MapFallbackToFile("index.html");

// Collect all local IPv4 addresses and machine name
var localIps = GetLocalIPv4Addresses();
var machineName = Environment.MachineName;

// Print startup banner
PrintBanner(port, localIps, machineName);

// Auto-open firewall & write server endpoint discovery file for shared folder clients
EnsureFirewallAndSharedConfig(port, localIps, machineName);

// SignalR heartbeat background task (every 5 seconds)
_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            await Task.Delay(5000);
            var hubContext = app.Services.GetRequiredService<IHubContext<MesHub>>();
            await hubContext.Clients.All.SendAsync("ServerHeartbeat", new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                machineCount = 0
            });
        }
        catch { }
    }
});

// Periodic Heartbeat Checker for Machines: If not heard from for 60s, mark as "대기" or "오프라인"
_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            await Task.Delay(15000);
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDb>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<MesHub>>();
            var machines = db.GetAllMachines();
            var now = DateTime.Now;

            foreach (var m in machines)
            {
                if (DateTime.TryParse(m.LastHeartbeat, out var lastHb))
                {
                    var diff = now - lastHb;
                    if (diff.TotalSeconds > 60 && m.Status != "정지" && m.Status != "에러")
                    {
                        db.UpdateMachineStatus(m.MachineId, "대기", m.CurrentProgram, m.SpindleRpm, m.FeedRate, m.ActiveJobNumber, m.ActivePartName, m.ActiveWorker);
                        await hub.Clients.All.SendAsync("MachineStatusChanged", new
                        {
                            machineId = m.MachineId,
                            status = "대기",
                            message = $"통신 응답 지연 (마지막 수신: {diff.TotalSeconds:F0}초 전)"
                        });
                    }
                }
            }
        }
        catch { }
    }
});

// Ensure Desktop Share Folders Exist for Manufacturing Team 3 (00-진행중, 99-완료백업)
try
{
    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    var pShared = Path.Combine(desktop, "제조3팀공유폴더", "00-진행중");
    var pBackup = Path.Combine(desktop, "제조3팀공유폴더", "99-완료백업");
    if (!Directory.Exists(pShared)) Directory.CreateDirectory(pShared);
    if (!Directory.Exists(pBackup)) Directory.CreateDirectory(pBackup);
}
catch { }


// Automatically open default web browser to the dashboard when server starts
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1000);
        try
        {
            var url = $"http://localhost:{port}";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    });
});

app.Run();

// -----------------------------------------------------------------------------
// Helper functions
// -----------------------------------------------------------------------------
static void EnsureFirewallAndSharedConfig(int port, List<string> localIps, string machineName)
{
    // 1. Write server_info.json and auto-populate adjacent client_config.json (for shared folder usage!)
    try
    {
        var primaryUrl = localIps.Count > 0 ? $"http://{localIps[0]}:{port}" : $"http://localhost:{port}";
        var serverInfo = new
        {
            PrimaryUrl = primaryUrl,
            HostUrl = $"http://{machineName}:{port}",
            AllUrls = localIps.Select(ip => $"http://{ip}:{port}").ToList(),
            MachineName = machineName,
            Port = port,
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        var infoJson = JsonSerializer.Serialize(serverInfo, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "server_info.json"), infoJson);

        // If adjacent 2_현장_작업자_클라이언트 folder exists, update its client_config.json automatically!
        var parentDir = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
        if (!string.IsNullOrEmpty(parentDir))
        {
            var rootInfoPath = Path.Combine(parentDir, "server_info.json");
            File.WriteAllText(rootInfoPath, infoJson);

            var adjClientDir = Path.Combine(parentDir, "2_현장_작업자_클라이언트");
            if (Directory.Exists(adjClientDir))
            {
                var clientCfgPath = Path.Combine(adjClientDir, "client_config.json");
                var cfgObj = new
                {
                    ServerUrl = primaryUrl,
                    LastUsername = "",
                    LastMachineId = "R01"
                };
                File.WriteAllText(clientCfgPath, JsonSerializer.Serialize(cfgObj, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }
    catch { }

    // 2. Attempt to open Windows Firewall silently in background (succeeds when run as Admin)
    _ = Task.Run(() =>
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CNCMes.Server.exe");
            string[] cmds =
            {
                "netsh advfirewall set allprofiles state off",
                $"netsh advfirewall firewall add rule name=\"CNC-MES Port {port}\" dir=in action=allow protocol=TCP localport={port} enable=yes",
                "netsh advfirewall firewall add rule name=\"CNC-MES Discovery 5002\" dir=in action=allow protocol=UDP localport=5002 enable=yes",
                $"netsh advfirewall firewall add rule name=\"CNC-MES Server App\" dir=in action=allow program=\"{exePath}\" enable=yes"
            };
            foreach (var cmd in cmds)
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {cmd}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                p?.WaitForExit(2000);
            }
        }
        catch { }
    });
}

static int FindAvailablePort(int startPort)
{
    for (int p = startPort; p < startPort + 20; p++)
    {
        if (IsPortAvailable(p)) return p;
    }
    return startPort;
}

static bool IsPortAvailable(int port)
{
    try
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
}

static List<string> GetLocalIPv4Addresses()
{
    var ips = new List<string>();
    try
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            {
                ips.Add(ip.ToString());
            }
        }
    }
    catch { }
    return ips;
}

static void PrintBanner(int port, List<string> localIps, string machineName)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("=================================================================");
    Console.WriteLine("   CNC-MES 통합 생산관리 서버 (CNCMes.Server v1.3.7)");
    Console.WriteLine("=================================================================");
    Console.ResetColor();
    Console.WriteLine($"  컴퓨터 이름 : {machineName}");
    Console.WriteLine($"  서비스 포트 : {port}");
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  [접속 가능한 주소 목록]");
    Console.WriteLine($"    - 로컬 접속:   http://localhost:{port}");
    Console.WriteLine($"    - 호스트명:    http://{machineName}:{port}");
    foreach (var ip in localIps)
    {
        Console.WriteLine($"    - 네트워크 IP: http://{ip}:{port}");
    }
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("  [공유폴더 자동 동기화]");
    Console.WriteLine(@"    - 바탕화면\제조3팀공유폴더 -진행중 (5초 주기 감지 및 자동등록)");
    Console.WriteLine(@"    - 바탕화면\제조3팀공유폴더\99-완료백업 (가공 완료 시 자동 백업보관)");
    Console.WriteLine();
    Console.WriteLine("  서버를 종료하려면 Ctrl+C 를 누르세요.");
    Console.WriteLine("=================================================================");
}
