using FashionSaaS.API.Constants;
using FashionSaaS.Application.AuditLogs;
using FashionSaaS.Application.AuditLogs.DTOs;
using FashionSaaS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "SuperAdmin")]
[Authorize(Policy = "MfaVerified")]
[EnableRateLimiting("SuperAdminPolicy")]
public class AuditLogsController(AuditLogQueryService auditLogService) : ControllerBase
{
    [HttpGet(ApiUrl.AdminAuditLogs.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] AuditLogFilterRequest filter)
    {
        ResponseData<PagedResult<AuditLogResponse>> response = await auditLogService.GetPagedAsync(filter);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.AdminAuditLogs.GetById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(Guid id)
    {
        ResponseData<AuditLogResponse> response = await auditLogService.GetByIdAsync(id);
        return StatusCode(response.StatusCode, response);
    }
}
