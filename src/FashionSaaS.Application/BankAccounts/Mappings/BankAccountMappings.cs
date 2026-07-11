using FashionSaaS.Application.BankAccounts.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.BankAccounts.Mappings;

public class BankAccountMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<BankAccount, BankAccountResponse>();
        config.NewConfig<BankAccount, BankAccountFullResponse>();
        config.NewConfig<CreateBankAccountRequest, BankAccount>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents)
            .Ignore(dest => dest.AccountTitleEncrypted)
            .Ignore(dest => dest.AccountNumberEncrypted)
            .Ignore(dest => dest.BankNameEncrypted)
            .Ignore(dest => dest.BranchCodeEncrypted)
            .Ignore(dest => dest.IbanEncrypted);
        config.NewConfig<UpdateBankAccountRequest, BankAccount>()
            .IgnoreNullValues(true)
            .Ignore(dest => dest.AccountTitleEncrypted)
            .Ignore(dest => dest.AccountNumberEncrypted)
            .Ignore(dest => dest.BankNameEncrypted)
            .Ignore(dest => dest.BranchCodeEncrypted)
            .Ignore(dest => dest.IbanEncrypted);
    }
}
