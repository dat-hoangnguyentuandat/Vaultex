namespace App.Application.DTOs
{
    public class PagedResult<T>
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<T> Items { get; set; } = [];
    }

    public class CreateProductDto
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(200, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.StringLength(2000)]
        public string? Description { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, 1_000_000_000)]
        public decimal Price { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
        public int Stock { get; set; }
    }

    public class ProductDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedByEmail { get; set; } = string.Empty;
    }
}
