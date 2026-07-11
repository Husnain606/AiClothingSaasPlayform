using FashionSaaS.Application.Tenants.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Tenants.Mappings;

public class TenantMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Tenant, TenantResponse>();
        config.NewConfig<CreateTenantRequest, Tenant>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents);
        config.NewConfig<UpdateTenantRequest, Tenant>()
            .IgnoreNullValues(true);
    }
}
