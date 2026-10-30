using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CNCMes.Server.Data;
using CNCMes.Server.Hubs;
using CNCMes.Server.Models;
using CNCMes.Server.Services;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PermanentEventsController : ControllerBase
{
    private readonly SqliteDb _db;
    private readonly IHubContext<MesHub> _hubContext;
    private readonly ILogger<PermanentEventsController> _logger;

    public PermanentEventsController(SqliteDb db, IHubContext<MesHub> hubContext, ILogger<PermanentEventsController> logger)
    {
        _db = db;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAll([FromQuery] string? category)
    {
        return Ok(_db.GetAllEvents(category));
    }

    // 핵심: 동일 품번으로 작업 시 과거 불량/이슈 이력 선제 경고 API
    [HttpGet("history/{partCode}")]
    public IActionResult GetHistoryByPartCode(string partCode)
    {
        if (string.IsNullOrWhiteSpace(partCode)) return BadRequest();
        var history = _db.GetEventsByPartCode(partCode);
        return Ok(new
        {
            partCode,
            totalIssueCount = history.Count,
            criticalCount = history.Count(x => x.EventType.Contains("파손") || x.EventType.Contains("불량") || x.EventType.Contains("간섭")),
            history
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] PermanentEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.PartCode) || string.IsNullOrWhiteSpace(ev.Description) || string.IsNullOrWhiteSpace(ev.EventType))
        {
            return BadRequest(new { message = "품번, 이벤트 유형, 발생 내용은 필수 항목입니다." });
        }

        try
        {
            var eventId = _db.CreatePermanentEvent(ev);

            ServerFileLogger.LogDefectAndCountermeasure(
                "신규등록",
                ev.PartCode,
                ev.MachineId,
                ev.EventCategory,
                ev.EventType,
                ev.Description,
                ev.Cause,
                ev.ActionTaken,
                ev.AuthorUsername);

            // SignalR 긴급 팝업 브로드캐스트
            await _hubContext.Clients.All.SendAsync("PermanentEventCreated", new
            {
                eventId,
                ev.PartCode,
                ev.EventCategory,
                ev.EventType,
                ev.Description,
                ev.Cause,
                ev.ActionTaken,
                ev.AuthorUsername
            });

            return Ok(new { eventId, message = "불량/수정 및 대책 히스토리가 영구 기록되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create permanent event");
            return StatusCode(500, new { message = "이벤트 등록 중 오류: " + ex.Message });
        }
    }

    [HttpPut("{eventId:int}")]
    public async Task<IActionResult> UpdateEvent(int eventId, [FromBody] PermanentEvent ev)
    {
        var ok = _db.UpdatePermanentEvent(eventId, ev);
        if (!ok) return NotFound(new { message = "해당 불량/대책 히스토리를 찾을 수 없습니다." });

        ServerFileLogger.LogDefectAndCountermeasure(
            $"수정(#{eventId})",
            ev.PartCode,
            ev.MachineId,
            ev.EventCategory,
            ev.EventType,
            ev.Description,
            ev.Cause,
            ev.ActionTaken,
            ev.AuthorUsername);

        await _hubContext.Clients.All.SendAsync("PermanentEventCreated", new
        {
            eventId,
            ev.PartCode,
            ev.EventCategory,
            ev.EventType,
            ev.Description,
            ev.Cause,
            ev.ActionTaken,
            ev.AuthorUsername
        });

        return Ok(new { eventId, message = "불량/수정 및 대책 히스토리가 업데이트되었습니다." });
    }

    [HttpPost("upload-attachment/{eventId:int}")]
    public async Task<IActionResult> UploadAttachment(int eventId, [FromForm] IFormFile file, [FromForm] string? annotations)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "업로드할 파일이 없습니다." });
        }

        try
        {
            var uploadDir = Path.Combine(_db.DataDirectory, "attachments");
            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var storedName = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(uploadDir, storedName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var fileType = (ext == ".jpg" || ext == ".png" || ext == ".jpeg" || ext == ".bmp") ? "IMAGE" :
                           (ext == ".pdf") ? "PDF" :
                           (ext == ".xlsx" || ext == ".csv" || ext == ".xls") ? "EXCEL" : "NC";

            // Save attachment record in DB
            using var conn = _db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO event_attachments (event_id, file_type, original_filename, stored_filename, file_size, annotations_json, uploaded_at)
                VALUES (@eid, @ft, @orig, @stored, @size, @ann, @now);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@eid", eventId);
            cmd.Parameters.AddWithValue("@ft", fileType);
            cmd.Parameters.AddWithValue("@orig", file.FileName);
            cmd.Parameters.AddWithValue("@stored", storedName);
            cmd.Parameters.AddWithValue("@size", file.Length);
            cmd.Parameters.AddWithValue("@ann", (object?)annotations ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            var attId = Convert.ToInt32(cmd.ExecuteScalar());

            return Ok(new { attachmentId = attId, originalFilename = file.FileName, storedFilename = storedName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload attachment");
            return StatusCode(500, new { message = "파일 업로드 오류: " + ex.Message });
        }
    }
}
