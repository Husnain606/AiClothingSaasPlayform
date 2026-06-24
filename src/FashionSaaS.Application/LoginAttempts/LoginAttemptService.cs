using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.LoginAttempts.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.LoginAttempts;

public class LoginAttemptService(ILoginAttemptRepository loginAttemptRepository)
{
    public async Task<ResponseData<PagedResult<LoginAttemptResponse>>> GetByEmailAsync(
        LoginAttemptFilterRequest filter)
    {
        if (string.IsNullOrEmpty(filter.Email))
            return ResponseData<PagedResult<LoginAttemptResponse>>.Failure("Email is required.", 400);

        var items = await loginAttemptRepository.GetByEmailAsync(filter.Email, 200);
        var filtered = items.AsEnumerable();
        if (filter.IsSuccess.HasValue)
            filtered = filtered.Where(a => a.IsSuccess == filter.IsSuccess);
        if (!string.IsNullOrEmpty(filter.IpAddress))
            filtered = filtered.Where(a => a.IpAddress == filter.IpAddress);

        var list = filtered.ToList();
        var paged = new PagedResult<LoginAttemptResponse>
        {
            Items = list.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
                .Select(Map).ToList(),
            TotalCount = list.Count,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
        return ResponseData<PagedResult<LoginAttemptResponse>>.Success(paged);
    }

    private static LoginAttemptResponse Map(UserLoginAttempt a) => new()
    {
        Id = a.Id, Email = a.Email, IpAddress = a.IpAddress,
        IsSuccess = a.IsSuccess, FailureReason = a.FailureReason, CreatedAt = a.CreatedAt
    };
}
