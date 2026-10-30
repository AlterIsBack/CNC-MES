using Microsoft.AspNetCore.Mvc;
using CNCMes.Server.Data;
using CNCMes.Server.Models;

namespace CNCMes.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingGroupsController : ControllerBase
{
    private readonly SqliteDb _db;

    public SettingGroupsController(SqliteDb db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(_db.GetAllSettingGroups());
    }

    [HttpPost]
    public IActionResult Create([FromBody] SettingGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.GroupName) || string.IsNullOrWhiteSpace(group.Material))
        {
            return BadRequest(new { message = "그룹명과 소재는 필수입니다." });
        }

        try
        {
            var id = _db.CreateSettingGroup(group);
            group.GroupId = id;
            return Ok(group);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "세팅 그룹 생성 중 오류: " + ex.Message });
        }
    }
}
