using System.Diagnostics.CodeAnalysis;
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application.Chat;
using FashionSaaS.TryOn.Infrastructure.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
// CA1515: MVC's default ControllerFeatureProvider only discovers public top-level classes
// (verified: dotnet/aspnetcore#12796) — an internal controller here is never routed, so this
// type must stay public despite the "no public API surface" rule the analyzer assumes.
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "ASP.NET Core MVC controller discovery requires public top-level classes.")]
public class ChatController(ChatService chatService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PostAsync([FromBody] ChatRequestDto dto, CancellationToken cancellationToken)
    {
        (var isSuccess, var statusCode, var message, ChatResultResponse? data) = await chatService.ReplyAsync(dto, cancellationToken);

        ResponseData<ChatResultResponse> response = isSuccess
            ? ResponseData<ChatResultResponse>.Success(data!, message, statusCode)
            : ResponseData<ChatResultResponse>.Failure(message, statusCode);

        return StatusCode(response.StatusCode, response);
    }
}
