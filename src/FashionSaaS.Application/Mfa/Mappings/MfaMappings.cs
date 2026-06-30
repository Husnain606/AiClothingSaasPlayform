using FashionSaaS.Domain.Entities;
using FashionSaaS.Application.Mfa.DTOs;
using Mapster;

namespace FashionSaaS.Application.Mfa.Mappings;

public class MfaMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<UserMfaSettings, MfaSetupResponse>();
    }
}
