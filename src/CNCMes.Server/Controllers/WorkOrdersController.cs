using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CNCMes.Server.Data;
using CNCMes.Server.Hubs;
using CNCMes.Server.Models;
using CNCMes.Server.Services;
using System.IO.Compression;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkOrdersController : ControllerBase
{
    private readonly SqliteDb _db;
    private readonly IHubContext<MesHub> _hubContext;
    private readonly ILogger<WorkOrdersController> _logger;

    public WorkOrdersController(SqliteDb db, IHubContext<MesHub> hubContext, ILogger<WorkOrdersController> logger)
    {
        _db = db;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAll([FromQuery] string? status, [FromQuery] bool includeHidden = false, [FromQuery] bool onlyHidden = false)
    {
        return Ok(_db.GetAllWorkOrders(status, includeHidden, onlyHidden));
    }

    [HttpPost("{id:int}/hide")]
    public async Task<IActionResult> HideOrder(int id)
    {
        var ok = _db.SetWorkOrderHidden(id, true);
        if (!ok) return NotFound(new { message = "해당 작업지시서를 찾을 수 없습니다." });
        var order = _db.GetWorkOrderById(id);
        ServerFileLogger.WriteLog("work_orders", $"[작지 숨김] 작지번호:{order?.OrderNumber ?? id.ToString()} | 처리:목록에서 숨김");
        await _hubContext.Clients.All.SendAsync("OrderUpdated", new { orderId = id, action = "HIDDEN", isHidden = true });
        return Ok(new { success = true, isHidden = true });
    }

    [HttpPost("{id:int}/unhide")]
    public async Task<IActionResult> UnhideOrder(int id)
    {
        var ok = _db.SetWorkOrderHidden(id, false);
        if (!ok) return NotFound(new { message = "해당 작업지시서를 찾을 수 없습니다." });
        var order = _db.GetWorkOrderById(id);
        ServerFileLogger.WriteLog("work_orders", $"[작지 표시 복원] 작지번호:{order?.OrderNumber ?? id.ToString()} | 처리:목록에 다시 표시");
        await _hubContext.Clients.All.SendAsync("OrderUpdated", new { orderId = id, action = "UNHIDDEN", isHidden = false });
        return Ok(new { success = true, isHidden = false });
    }

    [HttpGet("{id:int}")]
    public IActionResult GetById(int id)
    {
        var order = _db.GetWorkOrderById(id);
        if (order == null) return NotFound(new { message = "작업지시서를 찾을 수 없습니다." });
        return Ok(order);
    }

    [HttpGet("parts/{partId:int}")]
    public IActionResult GetPart(int partId)
    {
        var part = _db.GetPartById(partId);
        if (part == null) return NotFound(new { message = "부품 정보를 찾을 수 없습니다." });
        return Ok(part);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] WorkOrder order)
    {
        if (string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            return BadRequest(new { message = "작업지시서 번호는 필수입니다." });
        }

        if (order.Parts.Count == 0)
        {
            return BadRequest(new { message = "최소 1개 이상의 부품을 등록해야 합니다." });
        }

        try
        {
            var orderId = _db.CreateWorkOrder(order);
            var created = _db.GetWorkOrderById(orderId);

            // SignalR 브로드캐스트
            await _hubContext.Clients.All.SendAsync("OrderUpdated", new { orderId, action = "CREATED", orderNumber = order.OrderNumber });

            return Ok(created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create work order: {OrderNum}", order.OrderNumber);
            return StatusCode(500, new { message = "작업지시서 생성 중 오류가 발생했습니다: " + ex.Message });
        }
    }

    // =========================================================================
    // 작업 완료 처리 및 백업 (00-진행중 -> 99-완료백업 이동 또는 복사)
    // =========================================================================
    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> CompleteOrder(int id, [FromBody] CompleteOrderRequest? req)
    {
        var order = _db.GetWorkOrderById(id);
        if (order == null) return NotFound(new { message = "해당 작업지시서를 찾을 수 없습니다." });

        string? backupPath = order.BackupPath;

        // 원본 폴더가 존재하는 경우 [제조3팀공유폴더]-[99-완료백업]으로 자동 보관/이동
        try
        {
            if (!string.IsNullOrWhiteSpace(order.FolderPath) && Directory.Exists(order.FolderPath))
            {
                var folderName = Path.GetFileName(order.FolderPath);
                var parentDir = Directory.GetParent(order.FolderPath)?.FullName;
                string destBackupDir;

                if (!string.IsNullOrEmpty(parentDir) && Path.GetFileName(parentDir).Contains("진행중"))
                {
                    var shareRoot = Directory.GetParent(parentDir)?.FullName ?? parentDir;
                    destBackupDir = Path.Combine(shareRoot, "99-완료백업");
                }
                else
                {
                    destBackupDir = Path.Combine(AppContext.BaseDirectory, "backup_orders");
                }

                if (!Directory.Exists(destBackupDir)) Directory.CreateDirectory(destBackupDir);

                var targetPath = Path.Combine(destBackupDir, $"{folderName}_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.Move(order.FolderPath, targetPath);
                backupPath = targetPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to move completed folder: {Path}", order.FolderPath);
        }

        _db.UpdateWorkOrderStatus(id, "가공완료", backupPath);

        ServerFileLogger.WriteLog("work_orders", $"[가공완료] 작지번호:{order.OrderNumber} | 고객사:{order.CustomerName} | 백업위치:{backupPath ?? "-"} | 처리자:{req?.CompletedBy ?? "작업자"}");

        await _hubContext.Clients.All.SendAsync("OrderUpdated", new { orderId = id, action = "COMPLETED", orderNumber = order.OrderNumber });
        return Ok(new { success = true, backupPath });
    }

    // =========================================================================
    // 공유폴더 [제조3팀공유폴더]-[00-진행중] 자동 감지 및 신규 등록/동기화 API
    // =========================================================================
    [HttpGet("share-folder/sync")]
    [HttpPost("share-folder/sync")]
    public async Task<IActionResult> SyncShareFolder([FromQuery] string? customPath)
    {
        var resolvedPath = ResolveShareFolderPath(customPath);
        if (string.IsNullOrEmpty(resolvedPath) || !Directory.Exists(resolvedPath))
        {
            return Ok(new
            {
                success = false,
                message = "공유폴더 경로를 찾을 수 없습니다.",
                searchPath = resolvedPath ?? "없음",
                syncedCount = 0
            });
        }

        int synced = 0;
        var subDirs = Directory.GetDirectories(resolvedPath);

        foreach (var dir in subDirs)
        {
            var folderName = Path.GetFileName(dir).Trim();
            if (string.IsNullOrWhiteSpace(folderName)) continue;

            // 폴더명이 작지번호 (예: WO-2026-001, 20261029-01 등)
            var existing = _db.GetWorkOrderByNumber(folderName);
            if (existing == null)
            {
                // 내부 파일 목록 (.nc, .dwg, .dxf, .pdf, .txt 등)
                var files = Directory.GetFiles(dir);
                var notes = $"공유폴더 자동등록 (파일 {files.Length}개 포함: {string.Join(", ", files.Take(4).Select(Path.GetFileName))})";

                var newOrder = new WorkOrder
                {
                    OrderNumber = folderName,
                    CustomerName = "현장 의뢰",
                    TargetDeliveryDate = DateTime.Now.AddDays(3).ToString("yyyy-MM-dd"),
                    Status = "대기",
                    Notes = notes,
                    CreatedBy = "시스템 자동등록",
                    FolderPath = dir,
                    Parts = new List<WOPart>
                    {
                        new WOPart
                        {
                            PartCode = "1",
                            PartName = folderName,
                            Material = "AL6061",
                            Thickness = 10.0,
                            TargetQty = 1,
                            ProductionType = "단품"
                        }
                    }
                };

                try
                {
                    _db.CreateWorkOrder(newOrder);
                    synced++;
                    ServerFileLogger.WriteLog("work_orders", $"[공유폴더 자동등록] 작지번호:{folderName} | 위치:{dir}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to auto-create work order from folder: {Dir}", dir);
                }
            }
        }

        if (synced > 0)
        {
            await _hubContext.Clients.All.SendAsync("OrderUpdated", new { action = "SYNCED", count = synced });
        }

        return Ok(new
        {
            success = true,
            folderPath = resolvedPath,
            foundFolders = subDirs.Length,
            syncedCount = synced,
            message = $"{subDirs.Length}개 폴더 중 {synced}건의 신규 작업지시서가 자동 등록되었습니다."
        });
    }

    // =========================================================================
    // 작업지시서 연결 폴더 파일 목록 열람 (도면/NC/메모 파일 다시 찾아보기)
    // =========================================================================
    [HttpGet("{id:int}/files")]
    public IActionResult GetOrderFiles(int id)
    {
        var order = _db.GetWorkOrderById(id);
        if (order == null) return NotFound(new { message = "작업지시서를 찾을 수 없습니다." });

        var targetFolder = !string.IsNullOrEmpty(order.BackupPath) && Directory.Exists(order.BackupPath)
            ? order.BackupPath
            : order.FolderPath;

        if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
        {
            return Ok(new { exists = false, folder = targetFolder, files = Array.Empty<object>() });
        }

        var files = Directory.GetFiles(targetFolder)
            .Select(f => new
            {
                fileName = Path.GetFileName(f),
                fullPath = f,
                size = new FileInfo(f).Length,
                updatedAt = new FileInfo(f).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                isBackup = !string.IsNullOrEmpty(order.BackupPath) && targetFolder == order.BackupPath
            })
            .ToList();

        return Ok(new
        {
            exists = true,
            folder = targetFolder,
            isBackup = !string.IsNullOrEmpty(order.BackupPath) && targetFolder == order.BackupPath,
            completedAt = order.CompletedAt,
            files
        });
    }

    // =========================================================================
    // 📂 제조3팀 공유폴더 목록 조회 API (신규 작업지시서 등록 창에서 선택용)
    // =========================================================================
    [HttpGet("share-folder/folders")]
    public IActionResult GetShareFolderList([FromQuery] string? customPath)
    {
        var resolvedPath = ResolveShareFolderPath(customPath);
        if (string.IsNullOrEmpty(resolvedPath) || !Directory.Exists(resolvedPath))
        {
            return Ok(new
            {
                success = false,
                message = "공유폴더 경로를 찾을 수 없습니다.",
                searchPath = resolvedPath ?? "",
                folders = Array.Empty<object>()
            });
        }

        try
        {
            var subDirs = Directory.GetDirectories(resolvedPath);
            var resultList = new List<object>();

            foreach (var dir in subDirs)
            {
                var folderName = Path.GetFileName(dir).Trim();
                if (string.IsNullOrWhiteSpace(folderName)) continue;

                // 이미 등록된 작지인지 확인
                var existing = _db.GetWorkOrderByNumber(folderName);
                var files = Directory.GetFiles(dir);
                var sampleFiles = files.Take(5).Select(Path.GetFileName).ToList();

                resultList.Add(new
                {
                    folderName = folderName,
                    fullPath = dir,
                    fileCount = files.Length,
                    sampleFiles = sampleFiles,
                    isAlreadyRegistered = (existing != null),
                    existingOrderId = existing?.OrderId
                });
            }

            return Ok(new
            {
                success = true,
                folderPath = resolvedPath,
                folders = resultList
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading share folder list from {Path}", resolvedPath);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // =========================================================================
    // 📦 가공 완료 보고 및 후공정(핀삽입/각인/후처리 등) 등록 API
    // =========================================================================
    [HttpPost("post-process-report")]
    public async Task<IActionResult> ReportPostProcess([FromBody] PostProcessReportRequest req)
    {
        if (req == null || req.PartId <= 0)
        {
            return BadRequest(new { success = false, message = "유효하지 않은 요청 데이터입니다." });
        }

        try
        {
            var part = _db.GetPartById(req.PartId);
            if (part == null)
            {
                return NotFound(new { success = false, message = "해당 부품 정보를 찾을 수 없습니다." });
            }

            var report = new PostProcessReport
            {
                PartId = req.PartId,
                OrderId = req.OrderId > 0 ? req.OrderId : part.OrderId,
                OrderNumber = string.IsNullOrWhiteSpace(req.OrderNumber) ? (part.OrderNumber ?? "") : req.OrderNumber,
                PartName = string.IsNullOrWhiteSpace(req.PartName) ? (part.PartName ?? "") : req.PartName,
                MachineId = req.MachineId ?? "",
                Worker = req.Worker ?? "",
                CompletedQty = req.CompletedQty > 0 ? req.CompletedQty : part.TargetQty,
                SelectedProcesses = req.SelectedProcesses ?? "없음",
                Notes = req.Notes ?? "",
                Status = "후공정대기",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var reportId = _db.AddPostProcessReport(report);
            report.Id = reportId;

            // 부품 테이블의 완료수량 및 후공정 상태도 업데이트
            _db.UpdatePartPostProcess(req.PartId, report.CompletedQty, report.SelectedProcesses, report.Notes ?? "");

            ServerFileLogger.WriteLog("post_process", 
                $"[후공정등록] ID:{reportId} | 작지:{report.OrderNumber} | 품목:{report.PartName} | 수량:{report.CompletedQty} | 공정:{report.SelectedProcesses} | 작업자:{report.Worker} | 메모:{report.Notes}");

            // SignalR 실시간 알림 (클라이언트 및 웹 대시보드)
            await _hubContext.Clients.All.SendAsync("PostProcessReported", report);
            await _hubContext.Clients.All.SendAsync("OrderUpdated", new { action = "PART_POST_PROCESS", partId = req.PartId, reportId = reportId });

            return Ok(new
            {
                success = true,
                message = "가공 완료 및 후공정 요청이 성공적으로 등록되었습니다.",
                report = report
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting post-process report");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // =========================================================================
    // 📦 후공정 목록 조회 API
    // =========================================================================
    [HttpGet("post-process-reports")]
    public IActionResult GetPostProcessReports([FromQuery] string? status)
    {
        try
        {
            var reports = _db.GetPostProcessReports(status);
            return Ok(new { success = true, reports = reports });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching post-process reports");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // =========================================================================
    // 📦 후공정 완료 처리 API
    // =========================================================================
    [HttpPost("post-process-reports/{id:int}/complete")]
    public async Task<IActionResult> CompletePostProcess(int id, [FromBody] CompletePostProcessRequest? req)
    {
        try
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var ok = _db.UpdatePostProcessReportStatus(id, "후공정완료", now);
            if (!ok)
            {
                return NotFound(new { success = false, message = "해당 후공정 항목을 찾을 수 없습니다." });
            }

            ServerFileLogger.WriteLog("post_process", $"[후공정완료] ID:{id} | 완료자:{req?.CompletedBy ?? "담당자"} | 일시:{now}");

            await _hubContext.Clients.All.SendAsync("PostProcessCompleted", new { reportId = id, completedAt = now });
            await _hubContext.Clients.All.SendAsync("OrderUpdated", new { action = "POST_PROCESS_COMPLETED", reportId = id });

            return Ok(new { success = true, message = "후공정이 완료 처리되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing post process report {Id}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private static string ResolveShareFolderPath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
            return customPath;

        // 1. 바탕화면\제조3팀공유폴더 -진행중
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var p1 = Path.Combine(desktop, "제조3팀공유폴더", "00-진행중");
        if (Directory.Exists(p1)) return p1;

        // 2. OneDrive 바탕화면\제조3팀공유폴더 -진행중
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pOne1 = Path.Combine(userProfile, "OneDrive", "바탕 화면", "제조3팀공유폴더", "00-진행중");
        if (Directory.Exists(pOne1)) return pOne1;

        var pOne2 = Path.Combine(userProfile, "OneDrive", "Desktop", "제조3팀공유폴더", "00-진행중");
        if (Directory.Exists(pOne2)) return pOne2;

        // 3. 바탕화면 바로 아래 00-진행중
        var p2 = Path.Combine(desktop, "00-진행중");
        if (Directory.Exists(p2)) return p2;

        // 4. 실행 디렉터리 인근
        var baseDir = AppContext.BaseDirectory;
        var p3 = Path.Combine(baseDir, "제조3팀공유폴더", "00-진행중");
        if (Directory.Exists(p3)) return p3;

        // 5. 없으면 바탕화면에 제조3팀공유폴더 -진행중 및 99-완료백업 자동 생성
        try
        {
            var pDefault = Path.Combine(desktop, "제조3팀공유폴더", "00-진행중");
            var pBackup = Path.Combine(desktop, "제조3팀공유폴더", "99-완료백업");
            if (!Directory.Exists(pDefault)) Directory.CreateDirectory(pDefault);
            if (!Directory.Exists(pBackup)) Directory.CreateDirectory(pBackup);
            return pDefault;
        }
        catch { }

        return p1;
    }
}

public class CompleteOrderRequest
{
    public string? CompletedBy { get; set; }
    public string? Notes { get; set; }
}

public class PostProcessReportRequest
{
    public int PartId { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public string PartName { get; set; } = "";
    public string MachineId { get; set; } = "";
    public string Worker { get; set; } = "";
    public int CompletedQty { get; set; }
    public string SelectedProcesses { get; set; } = "";
    public string? Notes { get; set; }
}

public class CompletePostProcessRequest
{
    public string? CompletedBy { get; set; }
    public string? Notes { get; set; }
}
