using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CNCMes.Server.Data;
using CNCMes.Server.Hubs;
using CNCMes.Server.Models;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExecutionsController : ControllerBase
{
    private readonly SqliteDb _db;
    private readonly IHubContext<MesHub> _hubContext;
    private readonly ILogger<ExecutionsController> _logger;

    public ExecutionsController(SqliteDb db, IHubContext<MesHub> hubContext, ILogger<ExecutionsController> logger)
    {
        _db = db;
        _hubContext = hubContext;
        _logger = logger;
    }

    public record StartExecutionRequest(int StepId, int? AssignmentId, string MachineId, string WorkerUsername, int TargetQty, string? Notes);
    public record FinishExecutionRequest(int ExecutionId, int MachinedQty, string? Notes);

    [HttpPost("start")]
    public async Task<IActionResult> StartExecution([FromBody] StartExecutionRequest req)
    {
        if (req.StepId <= 0 || string.IsNullOrWhiteSpace(req.MachineId) || string.IsNullOrWhiteSpace(req.WorkerUsername))
        {
            return BadRequest(new { message = "공정 ID, 장비 ID, 작업자 정보는 필수입니다." });
        }

        try
        {
            var execId = _db.StartExecution(req.StepId, req.AssignmentId, req.MachineId, req.WorkerUsername, req.TargetQty, req.Notes);

            // Update machine status
            _db.UpdateMachineStatus(req.MachineId, "가공중", null, 0, 0, null, null, req.WorkerUsername);

            await _hubContext.Clients.All.SendAsync("ExecutionStarted", new { executionId = execId, req.StepId, req.MachineId, req.WorkerUsername });
            await _hubContext.Clients.All.SendAsync("MachineStatusChanged", new { machineId = req.MachineId, status = "가공중", worker = req.WorkerUsername });

            return Ok(new { executionId = execId, message = "가공이 시작되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start execution");
            return StatusCode(500, new { message = "가공 시작 처리 중 오류: " + ex.Message });
        }
    }

    [HttpPost("finish")]
    public async Task<IActionResult> FinishExecution([FromBody] FinishExecutionRequest req)
    {
        if (req.ExecutionId <= 0)
        {
            return BadRequest(new { message = "실행 ID가 올바르지 않습니다." });
        }

        try
        {
            _db.FinishExecution(req.ExecutionId, req.MachinedQty, req.Notes);

            await _hubContext.Clients.All.SendAsync("ExecutionFinished", new { executionId = req.ExecutionId, req.MachinedQty });
            return Ok(new { message = "가공이 완료되었으며 측정 대기 상태로 전이되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finish execution");
            return StatusCode(500, new { message = "가공 완료 처리 중 오류: " + ex.Message });
        }
    }

    [HttpGet("step/{stepId:int}")]
    public IActionResult GetExecutionsByStep(int stepId)
    {
        return Ok(_db.GetExecutionsByStepId(stepId));
    }
}
