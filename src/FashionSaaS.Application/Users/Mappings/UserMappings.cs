using FashionSaaS.Application.Users.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Users.Mappings;

public class UserMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<User, UserResponse>()
            .Map(dest => dest.Roles, src => src.UserRoles.Select(ur => ur.Role.Name.ToString()).ToList());
        config.NewConfig<CreateUserRequest, User>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents)
            .Ignore(dest => dest.PasswordHash)
            .Ignore(dest => dest.IsEmailVerified)
            .Ignore(dest => dest.IsLocked);
        config.NewConfig<UpdateUserRequest, User>()
            .IgnoreNullValues(true);
    }
}
