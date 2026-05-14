using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using App.Application.DTOs;

namespace App.Application.Interfaces
{
    public interface IProductService
    {
        Task<ProductDto> CreateProductAsync(CreateProductDto dto, Guid userId);
        Task<ProductDto?> GetProductByIdAsync(Guid id);
        Task<List<ProductDto>> GetAllProductsAsync();
        Task<ProductDto> UpdateProductAsync(Guid id, CreateProductDto dto, Guid userId);
        Task DeleteProductAsync(Guid id, Guid userId);
    }
}
