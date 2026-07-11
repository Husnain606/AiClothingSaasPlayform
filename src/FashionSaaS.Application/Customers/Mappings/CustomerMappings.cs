using FashionSaaS.Application.Customers.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Customers.Mappings;

public class CustomerMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Customer, CustomerResponse>();
        config.NewConfig<CreateCustomerRequest, Customer>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents)
            .Ignore(dest => dest.TenantId);
        config.NewConfig<UpdateCustomerRequest, Customer>()
            .IgnoreNullValues(true);
    }
}
