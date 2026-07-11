using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Inventory;
using FashionSaaS.Application.Inventory.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner,InventoryManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
internal class InventoryController(InventoryService inventoryService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpPost(ApiUrl.TenantInventory.AdjustStock)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockRequest request)
    {
        ResponseData<StockAdjustmentResponse> response = await inventoryService.AdjustStockAsync(request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantInventory.GetLowStock)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetLowStock([FromQuery] int threshold = InventoryService.LowStockThreshold)
    {
        ResponseData<IReadOnlyList<LowStockItemResponse>> response = await inventoryService.GetLowStockAsync(threshold);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantInventory.GetStockHistory)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStockHistory(Guid variantId)
    {
        ResponseData<IReadOnlyList<StockAdjustmentResponse>> response = await inventoryService.GetStockHistoryAsync(variantId);
        return StatusCode(response.StatusCode, response);
    }
}
