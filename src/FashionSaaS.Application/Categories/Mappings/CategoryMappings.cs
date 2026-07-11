using FashionSaaS.Application.Categories.DTOs;
using FashionSaaS.Domain.Entities;
using Mapster;

namespace FashionSaaS.Application.Categories.Mappings;

public class CategoryMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Category, CategoryResponse>();
        config.NewConfig<Category, CategoryTreeNode>()
            .Map(dest => dest.Children, src => src.Children.AsQueryable().ProjectToType<CategoryTreeNode>());
        config.NewConfig<CreateCategoryRequest, Category>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DomainEvents);
        config.NewConfig<UpdateCategoryRequest, Category>()
            .IgnoreNullValues(true);
        config.NewConfig<MoveCategoryRequest, Category>()
            .Map(dest => dest.ParentCategoryId, src => src.NewParentId);
    }
}
