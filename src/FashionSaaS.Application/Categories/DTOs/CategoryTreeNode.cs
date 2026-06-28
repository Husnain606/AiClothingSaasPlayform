namespace FashionSaaS.Application.Categories.DTOs;

public class CategoryTreeNode
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public IReadOnlyList<CategoryTreeNode> Children { get; set; } = new List<CategoryTreeNode>();
}
