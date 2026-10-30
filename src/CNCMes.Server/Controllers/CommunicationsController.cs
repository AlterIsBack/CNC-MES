using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CNCMes.Server.Data;
using CNCMes.Server.Hubs;
using CNCMes.Server.Models;
using CNCMes.Server.Services;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CommunicationsController : ControllerBase
{
    private readonly SqliteDb _db;
    private readonly IHubContext<MesHub> _hubContext;

    public CommunicationsController(SqliteDb db, IHubContext<MesHub> hubContext)
    {
        _db = db;
        _hubContext = hubContext;
    }

    #region Chat & Topics (Zulip형)
    [HttpGet("chat")]
    public IActionResult GetChat([FromQuery] string channelType = "GENERAL", [FromQuery] string topicKey = "공통")
    {
        return Ok(_db.GetMessagesByTopic(channelType, topicKey));
    }

    [HttpPost("chat")]
    public async Task<IActionResult> PostChat([FromBody] ChatMessage msg)
    {
        if (string.IsNullOrWhiteSpace(msg.Content) || string.IsNullOrWhiteSpace(msg.Author))
        {
            return BadRequest(new { message = "내용과 작성자는 필수입니다." });
        }

        var id = _db.AddChatMessage(msg);
        msg.Id = id;
        msg.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // Save chat to server file log (logs/chat_log_YYYY-MM-DD.txt & all_server_log)
        ServerFileLogger.LogChat(msg.ChannelType, msg.TopicKey, msg.Author, msg.Content);

        var groupName = $"{msg.ChannelType}:{msg.TopicKey}";
        await _hubContext.Clients.Group(groupName).SendAsync("ChatMessageReceived", msg);
        await _hubContext.Clients.All.SendAsync("ChatNotification", new { msg.Id, msg.ChannelType, msg.TopicKey, msg.Author, msg.Content, msg.CreatedAt });

        return Ok(msg);
    }

    [HttpPost("chat/promote-to-event")]
    public async Task<IActionResult> PromoteChatToEvent([FromBody] PermanentEvent ev, [FromQuery] int messageId)
    {
        var eventId = _db.CreatePermanentEvent(ev);
        if (messageId > 0)
        {
            _db.LinkChatToPermanentEvent(messageId, eventId);
        }

        await _hubContext.Clients.All.SendAsync("PermanentEventCreated", new { eventId, ev.PartCode, ev.EventType, ev.Description });
        return Ok(new { eventId, message = "채팅 내용이 공식 영구 이벤트로 승격 등록되었습니다." });
    }
    #endregion

    #region Notices
    [HttpGet("notices")]
    public IActionResult GetNotices()
    {
        return Ok(_db.GetAllNotices());
    }

    [HttpPost("notices")]
    public async Task<IActionResult> CreateNotice([FromBody] Notice notice)
    {
        if (string.IsNullOrWhiteSpace(notice.Title) || string.IsNullOrWhiteSpace(notice.Content))
        {
            return BadRequest(new { message = "공지 제목과 내용은 필수입니다." });
        }

        var id = _db.CreateNotice(notice);
        notice.NoticeId = id;
        notice.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        ServerFileLogger.LogNotice("등록", notice.Title, notice.Author, notice.Content);

        await _hubContext.Clients.All.SendAsync("NoticeCreated", notice);
        return Ok(notice);
    }

    [HttpPost("notices/{id:int}/ack")]
    public async Task<IActionResult> AcknowledgeNotice(int id, [FromQuery] string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return BadRequest();
        _db.AcknowledgeNotice(id, username);
        ServerFileLogger.LogNotice("클라이언트 확인서명", $"공지ID #{id}", username);
        await _hubContext.Clients.All.SendAsync("NoticeAcknowledged", new { noticeId = id, username, confirmedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
        return Ok(new { message = "공식 확인 서명이 완료되었습니다." });
    }
    #endregion

    #region Handovers (교대 인수인계)
    [HttpGet("handovers")]
    public IActionResult GetHandovers()
    {
        return Ok(_db.GetAllHandovers());
    }

    [HttpPost("handovers")]
    public async Task<IActionResult> CreateHandover([FromBody] HandoverLog log)
    {
        if (string.IsNullOrWhiteSpace(log.Summary) || string.IsNullOrWhiteSpace(log.SenderUsername))
        {
            return BadRequest(new { message = "인계 내용과 작성자는 필수입니다." });
        }

        var id = _db.CreateHandover(log);
        log.HandoverId = id;
        log.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        await _hubContext.Clients.All.SendAsync("HandoverCreated", log);
        return Ok(log);
    }

    [HttpPost("handovers/{id:int}/confirm")]
    public async Task<IActionResult> ConfirmHandover(int id, [FromQuery] string receiverUsername)
    {
        if (string.IsNullOrWhiteSpace(receiverUsername)) return BadRequest();
        _db.ConfirmHandover(id, receiverUsername);
        await _hubContext.Clients.All.SendAsync("HandoverConfirmed", new { handoverId = id, receiverUsername });
        return Ok(new { message = "인수 확인이 완료되었습니다." });
    }
    #endregion
}
