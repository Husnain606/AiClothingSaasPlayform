using System.Text;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Reports;
using FashionSaaS.Application.Reports.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner,StoreManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
internal class ReportsController(ReportService reportService) : ControllerBase
{
    [HttpGet(ApiUrl.TenantReports.Summary)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Summary([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] string? format = null)
    {
        ResponseData<SummaryReportDto> response = await reportService.GetSummaryAsync(from, to);
        if (string.Equals(format, "csv", StringComparison.Ordinal) && response.IsSuccess && response.Data is not null)
        {
            return File(Encoding.UTF8.GetBytes(CsvSerializer.Serialize(new[] { response.Data })),
                "text/csv; charset=utf-8", $"summary-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantReports.SalesOverTime)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SalesOverTime([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] ReportInterval interval = ReportInterval.Day, [FromQuery] string? format = null)
    {
        ResponseData<List<SalesPointDto>> response = await reportService.GetSalesOverTimeAsync(from, to, interval);
        if (string.Equals(format, "csv", StringComparison.Ordinal) && response.IsSuccess && response.Data is not null)
        {
            return File(Encoding.UTF8.GetBytes(CsvSerializer.Serialize(response.Data)),
                "text/csv; charset=utf-8", $"sales-over-time-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantReports.TopProducts)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TopProducts([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] int take = 10, [FromQuery] string by = "revenue", [FromQuery] string? format = null)
    {
        ResponseData<List<TopProductDto>> response = await reportService.GetTopProductsAsync(from, to, take, by);
        if (string.Equals(format, "csv", StringComparison.Ordinal) && response.IsSuccess && response.Data is not null)
        {
            return File(Encoding.UTF8.GetBytes(CsvSerializer.Serialize(response.Data)),
                "text/csv; charset=utf-8", $"top-products-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantReports.StatusBreakdown)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> StatusBreakdown([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] string? format = null)
    {
        ResponseData<List<StatusBreakdownDto>> response = await reportService.GetStatusBreakdownAsync(from, to);
        if (string.Equals(format, "csv", StringComparison.Ordinal) && response.IsSuccess && response.Data is not null)
        {
            return File(Encoding.UTF8.GetBytes(CsvSerializer.Serialize(response.Data)),
                "text/csv; charset=utf-8", $"order-status-breakdown-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantReports.CustomerAnalytics)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CustomerAnalytics([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] ReportInterval interval = ReportInterval.Day, [FromQuery] string? format = null)
    {
        ResponseData<CustomerAnalyticsDto> response = await reportService.GetCustomerAnalyticsAsync(from, to, interval);
        if (string.Equals(format, "csv", StringComparison.Ordinal) && response.IsSuccess && response.Data is not null)
        {
            return File(Encoding.UTF8.GetBytes(CsvSerializer.Serialize(response.Data.TopCustomers)),
                "text/csv; charset=utf-8", $"top-customers-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantReports.InventoryTrends)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> InventoryTrends([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] string? format = null)
    {
        ResponseData<InventoryTrendsDto> response = await reportService.GetInventoryTrendsAsync(from, to);
        if (string.Equals(format, "csv", StringComparison.Ordinal) && response.IsSuccess && response.Data is not null)
        {
            return File(Encoding.UTF8.GetBytes(CsvSerializer.Serialize(response.Data.LowStock)),
                "text/csv; charset=utf-8", $"low-stock-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        return StatusCode(response.StatusCode, response);
    }

    [HttpGet(ApiUrl.TenantReports.CategorySales)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CategorySales([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] Guid? categoryId = null, [FromQuery] string? format = null)
    {
        ResponseData<List<CategorySalesDto>> response = await reportService.GetCategorySalesAsync(from, to, categoryId);
        if (string.Equals(format, "csv", StringComparison.Ordinal) && response.IsSuccess && response.Data is not null)
        {
            return File(Encoding.UTF8.GetBytes(CsvSerializer.Serialize(response.Data)),
                "text/csv; charset=utf-8", $"category-sales-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        return StatusCode(response.StatusCode, response);
    }
}
