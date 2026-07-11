using System.Security.Claims;
using FashionSaaS.API.Constants;
using FashionSaaS.Application.Common;
using FashionSaaS.Application.ProductImages;
using FashionSaaS.Application.ProductImages.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionSaaS.API.Controllers.Tenant;

[ApiController]
[Authorize(Roles = "AdminOwner,StoreManager,ContentManager")]
[EnableRateLimiting("AuthenticatedPolicy")]
internal class ProductImagesController(ProductImageService imageService) : ControllerBase
{
    /// <summary>Maximum accepted upload size (5 MB).</summary>
    private const long MaxImageBytes = 5 * 1024 * 1024;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string Ua => Request.Headers.UserAgent.ToString();

    [HttpGet(ApiUrl.TenantProductImages.GetByProduct)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetByProduct(Guid productId)
    {
        ResponseData<IReadOnlyList<ProductImageResponse>> response = await imageService.GetByProductAsync(productId);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Uploads a product image as multipart/form-data. Content-type and size are validated at
    /// this boundary; the binary is streamed to the service which persists it via Cloudinary.
    /// </summary>
    [HttpPost(ApiUrl.TenantProductImages.Upload)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    [RequestSizeLimit(MaxImageBytes)]
    public async Task<IActionResult> Upload([FromForm] UploadImageForm form)
    {
        IFormFile? file = form.File;
        if (file is null || file.Length == 0)
            return StatusCode(400, ResponseData<string>.Failure("An image file is required.", 400));

        if (string.IsNullOrWhiteSpace(file.ContentType) ||
            !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(400, ResponseData<string>.Failure("Only image files are accepted.", 400));
        }

        if (file.Length > MaxImageBytes)
            return StatusCode(400, ResponseData<string>.Failure("Image must be 5 MB or smaller.", 400));

        var request = new UploadImageRequest
        {
            ProductId = form.ProductId,
            VariantId = form.VariantId,
            AltText = form.AltText
        };

        await using Stream stream = file.OpenReadStream();
        ResponseData<ProductImageResponse> response = await imageService.UploadAsync(request, stream, file.FileName, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut(ApiUrl.TenantProductImages.Reorder)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Reorder(Guid productId, [FromBody] ReorderImagesRequest request)
    {
        ResponseData<bool> response = await imageService.ReorderAsync(productId, request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost(ApiUrl.TenantProductImages.SetPrimary)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SetPrimary(Guid id)
    {
        var request = new SetPrimaryRequest { ImageId = id };
        ResponseData<bool> response = await imageService.SetPrimaryAsync(request, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    [HttpDelete(ApiUrl.TenantProductImages.Delete)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseData<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(Guid id)
    {
        ResponseData<bool> response = await imageService.DeleteAsync(id, UserId, Ip, Ua);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>multipart/form-data binding model: the uploaded file plus image metadata.</summary>
    internal class UploadImageForm
    {
        public IFormFile? File { get; set; }
        public Guid ProductId { get; set; }
        public Guid? VariantId { get; set; }
        public string? AltText { get; set; }
    }
}
