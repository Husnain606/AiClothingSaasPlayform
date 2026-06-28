namespace FashionSaaS.Application.Categories.DTOs;

public class MoveCategoryRequest
{
    public Guid Id { get; set; }
    public Guid? NewParentId { get; set; }
}
