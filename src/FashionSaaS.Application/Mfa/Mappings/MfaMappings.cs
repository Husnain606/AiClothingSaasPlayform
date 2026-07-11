using FashionSaaS.Application.Mfa.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Mfa.Mappings;

public class MfaMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<UserMfaSettings, MfaSetupResponse>();
    }
}
