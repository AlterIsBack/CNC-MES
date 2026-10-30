using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CNCMes.Server.Data;
using CNCMes.Server.Hubs;
using CNCMes.Server.Models;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InspectionsController : ControllerBase
{
    private readonly SqliteDb _db;
    private readonly IHubContext<MesHub> _hubContext;
    private readonly ILogger<InspectionsController> _logger;

    public InspectionsController(SqliteDb db, IHubContext<MesHub> hubContext, ILogger<InspectionsController> logger)
    {
        _db = db;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet("part/{partId:int}")]
    public IActionResult GetInspectionsByPart(int partId)
    {
        return Ok(_db.GetInspectionsByPartId(partId));
    }

    [HttpPost]
    public async Task<IActionResult> RecordInspection([FromBody] InspectionRecord record)
    {
        if (record.StepId <= 0 || record.PartId <= 0 || string.IsNullOrWhiteSpace(record.InspectorUsername))
        {
            return BadRequest(new { message = "공정 ID, 부품 ID, 검사자 정보는 필수입니다." });
        }

        try
        {
            var inspId = _db.RecordInspection(record);

            await _hubContext.Clients.All.SendAsync("InspectionRecorded", new
            {
                inspectionId = inspId,
                record.PartId,
                record.StepId,
                record.ResultVerdict,
                record.PassedQty,
                record.FailedQty
            });

            return Ok(new
            {
                inspectionId = inspId,
                message = $"측정 결과({record.ResultVerdict})가 성공적으로 등록되었습니다."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record inspection");
            return StatusCode(500, new { message = "측정 기록 등록 중 오류: " + ex.Message });
        }
    }
}
