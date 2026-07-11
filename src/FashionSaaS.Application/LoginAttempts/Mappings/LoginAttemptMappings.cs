using FashionSaaS.Application.LoginAttempts.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.LoginAttempts.Mappings;

public class LoginAttemptMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<UserLoginAttempt, LoginAttemptResponse>();
    }
}
