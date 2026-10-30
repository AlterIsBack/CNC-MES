using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CNCMes.Server.Data;
using CNCMes.Server.Hubs;
using CNCMes.Server.Models;
using CNCMes.Server.Services;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MachinesController : ControllerBase
{
    private readonly SqliteDb _db;
    private readonly IHubContext<MesHub> _hubContext;

    public MachinesController(SqliteDb db, IHubContext<MesHub> hubContext)
    {
        _db = db;
        _hubContext = hubContext;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(_db.GetAllMachines());
    }

    public record MachineUpdateDto(string Status, string? CurrentProgram, int SpindleRpm, double FeedRate, string? ActiveJobNumber, string? ActivePartName, string? ActiveWorker);

    [HttpPost("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] MachineUpdateDto req)
    {
        _db.UpdateMachineStatus(id, req.Status, req.CurrentProgram, req.SpindleRpm, req.FeedRate, req.ActiveJobNumber, req.ActivePartName, req.ActiveWorker);
        ServerFileLogger.LogWorkHistory(id, req.ActiveJobNumber ?? "-", req.ActivePartName ?? "-", req.CurrentProgram ?? "-", req.Status, req.ActiveWorker ?? "작업자");

        await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new
        {
            machineId = id,
            req.Status,
            req.CurrentProgram,
            req.SpindleRpm,
            req.FeedRate,
            req.ActiveJobNumber,
            req.ActivePartName,
            req.ActiveWorker
        });
        return Ok(new { message = "장비 상태가 업데이트되었습니다." });
    }

    [HttpPost]
    public async Task<IActionResult> AddMachine([FromBody] MachineStatus m)
    {
        if (string.IsNullOrWhiteSpace(m.MachineId) || string.IsNullOrWhiteSpace(m.MachineName))
            return BadRequest(new { message = "장비 코드(ID)와 장비명은 필수입니다." });

        _db.AddMachine(m);
        ServerFileLogger.LogWorkHistory(m.MachineId, "-", "-", $"신규 장비 추가 ({m.MachineName} / {m.ModelType})", "대기", "관리자");
        await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new { machineId = m.MachineId, action = "ADDED" });
        return Ok(new { message = $"장비 [{m.MachineId}] 추가 완료" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMachine(string id)
    {
        _db.DeleteMachine(id);
        ServerFileLogger.LogWorkHistory(id, "-", "-", "장비 삭제됨", "삭제", "관리자");
        await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new { machineId = id, action = "DELETED" });
        return Ok(new { message = $"장비 [{id}] 삭제 완료" });
    }

    [HttpGet("work-logs")]
    public IActionResult GetWorkLogs([FromQuery] string? machineId = null, [FromQuery] int limit = 60)
    {
        return Ok(_db.GetMachineWorkLogs(machineId, limit));
    }

    [HttpPut("work-logs/{id:int}")]
    public async Task<IActionResult> UpdateWorkLog(int id, [FromBody] MachineWorkLog updated, [FromQuery] string? modifiedBy = null)
    {
        var (before, after) = _db.UpdateMachineWorkLog(id, updated);
        if (before == null || after == null)
            return NotFound(new { message = "수정할 작업 이력을 찾을 수 없습니다." });

        var editor = string.IsNullOrWhiteSpace(modifiedBy) ? updated.WorkerName : modifiedBy;
        var beforeStr = $"장비:{before.MachineId}, 작지:{before.OrderNumber}, 품목:{before.PartName}, 내용:{before.WorkDetail}, 상태:{before.Status}";
        var afterStr = $"장비:{after.MachineId}, 작지:{after.OrderNumber}, 품목:{after.PartName}, 내용:{after.WorkDetail}, 상태:{after.Status}";

        ServerFileLogger.LogHistoryModification("수정", id, after.MachineId, beforeStr, afterStr, editor);

        // Also sync the machine's current active state if this is the latest log for that machine
        var latestLogs = _db.GetMachineWorkLogs(after.MachineId, 1);
        if (latestLogs.Count > 0 && latestLogs[0].Id == after.Id)
        {
            int rpm = after.Status == "가공중" ? 8000 : 0;
            double feed = after.Status == "가공중" ? 1500.0 : 0;
            // Update machines table without inserting a duplicate log row
            _db.AddMachine(new MachineStatus
            {
                MachineId = after.MachineId,
                MachineName = _db.GetAllMachines().FirstOrDefault(x => x.MachineId == after.MachineId)?.MachineName ?? after.MachineId,
                Status = after.Status
            });
        }

        await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new { machineId = after.MachineId, action = "LOG_UPDATED" });
        return Ok(after);
    }

    [HttpDelete("work-logs/{id:int}")]
    public async Task<IActionResult> DeleteWorkLog(int id, [FromQuery] string? deletedBy = null)
    {
        var before = _db.DeleteMachineWorkLog(id);
        if (before == null)
            return NotFound(new { message = "삭제할 작업 이력을 찾을 수 없습니다." });

        var editor = string.IsNullOrWhiteSpace(deletedBy) ? "작업자" : deletedBy;
        var beforeStr = $"장비:{before.MachineId}, 작지:{before.OrderNumber}, 품목:{before.PartName}, 내용:{before.WorkDetail}, 상태:{before.Status}";

        ServerFileLogger.LogHistoryModification("오클릭삭제", id, before.MachineId, beforeStr, "삭제됨", editor);

        await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new { machineId = before.MachineId, action = "LOG_DELETED" });
        return Ok(new { message = "잘못 입력된 작업 이력이 삭제되었으며 서버 로그에 기록되었습니다." });
    }

    [HttpPost("manual-log")]
    public async Task<IActionResult> AddManualWorkLog([FromBody] MachineWorkLog log)
    {
        if (string.IsNullOrWhiteSpace(log.MachineId) || string.IsNullOrWhiteSpace(log.WorkDetail))
            return BadRequest(new { message = "장비와 작업 내용은 필수입니다." });

        var status = string.IsNullOrWhiteSpace(log.Status) ? "가공중" : log.Status;
        int rpm = status == "가공중" ? 8000 : (status == "단도리/세팅" ? 1200 : 0);
        double feed = status == "가공중" ? 1500.0 : (status == "단도리/세팅" ? 300.0 : 0.0);

        // Update machine active status + insert into machine_work_logs
        _db.UpdateMachineStatus(log.MachineId, status, log.WorkDetail, rpm, feed, log.OrderNumber, log.PartName, log.WorkerName);
        ServerFileLogger.LogWorkHistory(log.MachineId, log.OrderNumber, log.PartName, log.WorkDetail, status, log.WorkerName, log.SubWorkerName, log.Qty);

        await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new
        {
            machineId = log.MachineId,
            Status = status,
            CurrentProgram = log.WorkDetail,
            ActiveJobNumber = log.OrderNumber,
            ActivePartName = log.PartName,
            ActiveWorker = log.WorkerName
        });

        return Ok(new { message = "수동 작업 이력이 장비 및 메인 서버에 기록되었습니다." });
    }

    [HttpGet("subtasks")]
    public IActionResult GetSubTasks([FromQuery] string? leader = null)
    {
        return Ok(_db.GetSubWorkerTasks(leader));
    }

    [HttpPost("subtasks")]
    public async Task<IActionResult> CreateSubTask([FromBody] SubWorkerTask task)
    {
        if (string.IsNullOrWhiteSpace(task.MachineId) || string.IsNullOrWhiteSpace(task.SubWorkerName) || string.IsNullOrWhiteSpace(task.Instruction))
            return BadRequest(new { message = "장비, 서브인원 이름, 지시 내용은 필수입니다." });

        var id = _db.CreateSubWorkerTask(task);
        task.TaskId = id;

        // Also record instruction into machine_work_logs so history reflects sub-worker assignment
        _db.AddMachineWorkLog(new MachineWorkLog
        {
            MachineId = task.MachineId,
            OrderNumber = string.IsNullOrWhiteSpace(task.OrderNumber) ? "-" : task.OrderNumber,
            PartName = string.IsNullOrWhiteSpace(task.PartName) ? "-" : task.PartName,
            WorkDetail = $"[서브지시->{task.SubWorkerName}] {task.Instruction}",
            Status = "작업지시",
            WorkerName = task.LeaderUsername,
            SubWorkerName = task.SubWorkerName,
            Qty = task.ReportedQty
        });

        ServerFileLogger.LogSubWorkerReport(task.MachineId, task.LeaderUsername, task.SubWorkerName, task.OrderNumber, task.PartName, task.Instruction, null, 0, "지시등록");
        await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new { machineId = task.MachineId, action = "SUBTASK_CREATED" });

        return Ok(task);
    }

    public record SubTaskReportDto(string ReportContent, int ReportedQty, string? MachineStatus);

    [HttpPost("subtasks/{taskId:int}/report")]
    public async Task<IActionResult> ReportSubTask(int taskId, [FromBody] SubTaskReportDto req)
    {
        var updated = _db.ReportSubWorkerTask(taskId, req.ReportContent, req.ReportedQty, "중앙보고완료");
        if (updated == null) return NotFound(new { message = "해당 지시 건을 찾을 수 없습니다." });

        var status = string.IsNullOrWhiteSpace(req.MachineStatus) ? "가공중" : req.MachineStatus;
        int rpm = status == "가공중" ? 8000 : 0;
        double feed = status == "가공중" ? 1500.0 : 0;

        string summaryWork = $"[서브보고:{updated.SubWorkerName}] {req.ReportContent}" + (req.ReportedQty > 0 ? $" ({req.ReportedQty}EA)" : "");
        string workerLabel = $"{updated.LeaderUsername}(서브:{updated.SubWorkerName})";

        _db.UpdateMachineStatus(updated.MachineId, status, summaryWork, rpm, feed, updated.OrderNumber, updated.PartName, workerLabel);
        ServerFileLogger.LogSubWorkerReport(updated.MachineId, updated.LeaderUsername, updated.SubWorkerName, updated.OrderNumber, updated.PartName, updated.Instruction, req.ReportContent, req.ReportedQty, "중앙보고완료");

        await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new
        {
            machineId = updated.MachineId,
            Status = status,
            CurrentProgram = summaryWork,
            ActiveJobNumber = updated.OrderNumber,
            ActivePartName = updated.PartName,
            ActiveWorker = workerLabel
        });

        return Ok(updated);
    }

    // =========================================================================
    // 사용 공구 이력 API (주요 가공 시 사용 공구/메이커/공구경/날장/재질/기타 기록)
    // =========================================================================
    [HttpGet("used-tools")]
    public IActionResult GetUsedTools([FromQuery] string? machineId = null, [FromQuery] int limit = 60)
    {
        return Ok(_db.GetMachineUsedTools(machineId, limit));
    }

    [HttpPost("used-tools")]
    public async Task<IActionResult> AddUsedTool([FromBody] MachineUsedTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.MachineId) || string.IsNullOrWhiteSpace(tool.ToolName))
        {
            return BadRequest(new { message = "장비 번호와 공구명은 필수입니다." });
        }

        var id = _db.AddMachineUsedTool(tool);
        tool.ToolLogId = id;

        // 서버 텍스트 파일 로그에도 영구 보존
        var logDetail = $"공구:{tool.ToolName}, 메이커:{tool.Maker}, 규격:{tool.ToolDiameter}, 날장:{tool.FluteLength}, 재질:{tool.Material}, 기타:{tool.Notes}";
        ServerFileLogger.WriteLog("used_tools", $"[사용공구등록] 장비:{tool.MachineId} | 작지:{tool.OrderNumber} | 품목:{tool.PartName} | {logDetail} | 작업자:{tool.WorkerName}");

        await _hubContext.Clients.All.SendAsync("UsedToolRecorded", tool);
        return Ok(tool);
    }

    [HttpDelete("used-tools/{id:int}")]
    public async Task<IActionResult> DeleteUsedTool(int id, [FromQuery] string? deletedBy = null)
    {
        var ok = _db.DeleteMachineUsedTool(id);
        if (!ok) return NotFound(new { message = "삭제할 공구 기록을 찾을 수 없습니다." });

        ServerFileLogger.WriteLog("used_tools", $"[사용공구삭제] ID:{id} 삭제됨 by {deletedBy ?? "작업자"}");
        await _hubContext.Clients.All.SendAsync("UsedToolDeleted", new { id });
        return Ok(new { success = true });
    }
}
