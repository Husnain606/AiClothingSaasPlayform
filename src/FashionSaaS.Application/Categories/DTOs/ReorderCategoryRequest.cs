namespace FashionSaaS.Application.Categories.DTOs;

public class ReorderCategoryRequest
{
    public IReadOnlyList<CategoryOrderItem> Items { get; set; } = new List<CategoryOrderItem>();
}

public class CategoryOrderItem
{
    public Guid Id { get; set; }
    public int SortOrder { get; set; }
}
