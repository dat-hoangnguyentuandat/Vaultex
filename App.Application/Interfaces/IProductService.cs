using App.Application.DTOs;

namespace App.Application.Interfaces
{
    public interface IProductService
    {
        Task<ProductDto> CreateProductAsync(CreateProductDto dto, Guid userId);
        Task<ProductDto?> GetProductByIdAsync(Guid id);
        Task<PagedResult<ProductDto>> GetAllProductsAsync(int page = 1, int pageSize = 20);
        Task<ProductDto> UpdateProductAsync(Guid id, CreateProductDto dto, Guid userId);
        Task DeleteProductAsync(Guid id, Guid userId);
    }
}
