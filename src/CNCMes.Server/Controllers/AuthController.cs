using Microsoft.AspNetCore.Mvc;
using CNCMes.Server.Data;
using CNCMes.Server.Models;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SqliteDb _db;
    private readonly ILogger<AuthController> _logger;

    public AuthController(SqliteDb db, ILogger<AuthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            initialized = _db.HasAnyUser(),
            systemVersion = "v1.2.2",
            serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    public record LoginRequest(string Username, string Password);
    public record CreateUserRequest(string Username, string Password, string? DisplayName, string? Role);
    public record RegisterRequest(string Username, string Password, string? DisplayName);

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { message = "아이디와 비밀번호를 입력해주세요." });
        }

        var user = _db.GetUserByUsername(req.Username);
        if (user == null || user.IsActive == 0)
        {
            return Unauthorized(new { message = "등록되지 않았거나 비활성화된 계정입니다." });
        }

        if (!SqliteDb.VerifyPassword(req.Password, user.PasswordSalt, user.PasswordHash))
        {
            return Unauthorized(new { message = "비밀번호가 올바르지 않습니다." });
        }

        _db.UpdateLastLogin(user.Username);

        return Ok(new
        {
            username = user.Username,
            displayName = user.DisplayName,
            role = user.Role
        });
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { message = "아이디와 비밀번호는 필수 입력 항목입니다." });
        }

        var existing = _db.GetUserByUsername(req.Username);
        if (existing != null)
        {
            return BadRequest(new { message = "이미 사용 중인 아이디입니다. 다른 아이디를 입력하세요." });
        }

        var ok = _db.CreateUser(req.Username, req.Password, req.DisplayName ?? req.Username, "WORKER");
        if (!ok)
        {
            return StatusCode(500, new { message = "계정 생성 중 오류가 발생했습니다." });
        }

        return Ok(new
        {
            message = "회원가입이 완료되었습니다! 등록하신 아이디로 로그인하세요.",
            username = req.Username,
            displayName = req.DisplayName ?? req.Username,
            role = "WORKER"
        });
    }

    [HttpPost("setup-admin")]
    public IActionResult SetupAdmin([FromBody] CreateUserRequest req)
    {
        if (_db.HasAnyUser())
        {
            return BadRequest(new { message = "이미 시스템 계정이 존재합니다." });
        }

        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { message = "관리자 아이디와 비밀번호를 모두 입력해주세요." });
        }

        var ok = _db.CreateUser(req.Username, req.Password, req.DisplayName ?? "시스템 관리자", "ADMIN");
        if (!ok)
        {
            return StatusCode(500, new { message = "관리자 계정 생성에 실패했습니다." });
        }

        return Ok(new { message = "관리자 계정이 성공적으로 등록되었습니다." });
    }

    [HttpGet("users")]
    public IActionResult GetUsers()
    {
        return Ok(_db.GetAllUsers());
    }

    [HttpPost("users")]
    public IActionResult CreateUser([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { message = "아이디와 비밀번호는 필수입니다." });
        }

        var ok = _db.CreateUser(req.Username, req.Password, req.DisplayName ?? req.Username, req.Role ?? "WORKER");
        if (!ok)
        {
            return BadRequest(new { message = "동일한 아이디가 이미 존재합니다." });
        }

        return Ok(new { message = "사용자가 성공적으로 등록되었습니다." });
    }
}
