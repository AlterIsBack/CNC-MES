using Microsoft.AspNetCore.Mvc;
using CNCMes.Server.Data;
using CNCMes.Server.Models;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly SqliteDb _db;
    private readonly ILogger<UsersController> _logger;

    public UsersController(SqliteDb db, ILogger<UsersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(_db.GetAllUsers());
    }

    public record RoleUpdateDto(string Role);
    public record PasswordResetDto(string NewPassword);
    public record StatusUpdateDto(int IsActive);

    [HttpPut("{id:int}/role")]
    public IActionResult UpdateRole(int id, [FromBody] RoleUpdateDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Role)) return BadRequest();
        var ok = _db.UpdateUserRole(id, req.Role.ToUpperInvariant());
        if (!ok) return NotFound();
        return Ok(new { message = "사용자 권한이 성공적으로 변경되었습니다." });
    }

    [HttpPut("{id:int}/password")]
    public IActionResult ResetPassword(int id, [FromBody] PasswordResetDto req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword)) return BadRequest();
        var ok = _db.ResetUserPassword(id, req.NewPassword);
        if (!ok) return NotFound();
        return Ok(new { message = "비밀번호가 성공적으로 재설정되었습니다." });
    }

    [HttpPut("{id:int}/status")]
    public IActionResult UpdateStatus(int id, [FromBody] StatusUpdateDto req)
    {
        var ok = _db.ToggleUserActive(id, req.IsActive);
        if (!ok) return NotFound();
        return Ok(new { message = "사용자 활성 상태가 변경되었습니다." });
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        var ok = _db.DeleteUser(id);
        if (!ok) return BadRequest(new { message = "관리자 계정이거나 존재하지 않는 사용자입니다." });
        return Ok(new { message = "사용자 계정이 삭제되었습니다." });
    }
}
