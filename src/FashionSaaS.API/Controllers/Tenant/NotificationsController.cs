using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Application.Notifications.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner,StoreManager,InventoryManager,OrderManager,ContentManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
public class NotificationsController(NotificationService notificationService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet(ApiUrl.TenantNotifications.GetAll)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll([FromQuery] NotificationFilter filter)
    {
        filter.RecipientUserId = UserId;
        ResponseData<PagedResult<NotificationResponse>> response = await notificationService.GetPagedAsync(filter);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantNotifications.GetUnreadCount)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUnreadCount()
    {
        ResponseData<int> response = await notificationService.GetUnreadCountAsync(UserId);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantNotifications.MarkRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        ResponseData<bool> response = await notificationService.MarkReadAsync(id, UserId);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantNotifications.MarkAllRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MarkAllRead()
    {
        ResponseData<bool> response = await notificationService.MarkAllReadAsync(UserId);
        return StatusCode(response.StatusCode, response);
    }
}
