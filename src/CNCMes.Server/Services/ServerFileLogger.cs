using System.Text;

namespace CNCMes.Server.Services;

public static class ServerFileLogger
{
    private static readonly object _lock = new();

    public static string LogDirPath => GetLogsDirectory();

    public static string GetLogsDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public static void WriteLog(string category, string message)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        WriteLineToFile($"{category}_{date}.txt", message);
    }

    private static void WriteLineToFile(string fileName, string line)
    {
        try
        {
            lock (_lock)
            {
                var dir = GetLogsDirectory();
                var filePath = Path.Combine(dir, fileName);
                var timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}";
                File.AppendAllText(filePath, timestamped, new UTF8Encoding(false));

                var allPath = Path.Combine(dir, $"all_server_log_{DateTime.Now:yyyy-MM-dd}.txt");
                if (!string.Equals(filePath, allPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.AppendAllText(allPath, timestamped, new UTF8Encoding(false));
                }
            }
        }
        catch { }
    }

    public static void LogChat(string channelType, string topicKey, string author, string content)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        WriteLineToFile($"chat_log_{date}.txt", $"[채팅][{channelType}:{topicKey}] 작성자:{author} | 내용: {content}");
    }

    public static void LogWorkHistory(string machineId, string? orderNo, string? partName, string workDetail, string status, string worker, string? subWorker = null, int qty = 0)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var ord = string.IsNullOrWhiteSpace(orderNo) ? "-" : orderNo;
        var prt = string.IsNullOrWhiteSpace(partName) ? "-" : partName;
        var wkr = string.IsNullOrWhiteSpace(subWorker) ? worker : $"{worker}(서브:{subWorker})";
        WriteLineToFile($"work_history_{date}.txt", $"[작업이력][장비:{machineId}][상태:{status}] 작지:{ord} | 품목:{prt} | 작업자:{wkr} | 수량:{qty} | 내용: {workDetail}");
    }

    public static void LogSubWorkerReport(string machineId, string leader, string subWorker, string? orderNo, string? partName, string instruction, string? reportContent, int qty, string status)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var ord = string.IsNullOrWhiteSpace(orderNo) ? "-" : orderNo;
        var prt = string.IsNullOrWhiteSpace(partName) ? "-" : partName;
        WriteLineToFile($"subworker_report_{date}.txt", $"[서브인원보고][장비:{machineId}][{status}] 지시자:{leader} -> 서브인원:{subWorker} | 작지:{ord} | 품목:{prt} | 지시:{instruction} | 보고:{reportContent ?? "-"} | 수량:{qty}");
    }

    public static void LogNotice(string action, string title, string username, string? content = null)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        WriteLineToFile($"notice_log_{date}.txt", $"[공지사항:{action}] 사용자:{username} | 제목:{title} {(string.IsNullOrWhiteSpace(content) ? "" : "| 내용:" + content)}");
    }

    public static void LogHistoryModification(string actionType, int logId, string machineId, string beforeState, string afterState, string modifiedBy)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var line = $"[이력{actionType}][ID:#{logId}][장비:{machineId}] 수정자:{modifiedBy} | 변경전:({beforeState}) => 변경후:({afterState})";
        WriteLineToFile($"work_history_edit_{date}.txt", line);
        WriteLineToFile($"work_history_{date}.txt", line);
    }

    public static void LogDefectAndCountermeasure(string actionType, string partCode, string? machineId, string category, string eventType, string description, string? cause, string? actionTaken, string author)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var line = $"[불량/수정/대책:{actionType}][장비:{machineId ?? "-"}][품번/품목:{partCode}][{category}/{eventType}] 작성자:{author} | 불량/수정내용:{description} | 원인:{cause ?? "-"} | 대책/조치:{actionTaken ?? "-"}";
        WriteLineToFile($"defect_countermeasure_{date}.txt", line);
    }
}
