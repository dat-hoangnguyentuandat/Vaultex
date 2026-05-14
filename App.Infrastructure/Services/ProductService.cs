using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using App.Application.DTOs;
using App.Application.Interfaces;
using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Services
{
    public class ProductService : IProductService
    {
        private readonly AppDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ITenantContext _tenantContext;

        public ProductService(AppDbContext context, UserManager<User> userManager, ITenantContext tenantContext)
        {
            _context = context;
            _userManager = userManager;
            _tenantContext = tenantContext;
        }

        public async Task<ProductDto> CreateProductAsync(CreateProductDto dto, Guid userId)
        {
            if (!_tenantContext.TenantId.HasValue)
                throw new InvalidOperationException("Tenant context is required to create a product.");

            var product = new Product
            {
                TenantId = _tenantContext.TenantId.Value,
                Name = dto.Name,
                Description = dto.Description,
                Price = dto.Price,
                Stock = dto.Stock,
                CreatedByUserId = userId
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            var user = await _userManager.FindByIdAsync(userId.ToString());
            return MapToDto(product, user?.Email ?? "Unknown");
        }

        public async Task<ProductDto?> GetProductByIdAsync(Guid id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return null;

            var user = await _userManager.FindByIdAsync(product.CreatedByUserId.ToString());
            return MapToDto(product, user?.Email ?? "Unknown");
        }

        public async Task<List<ProductDto>> GetAllProductsAsync()
        {
            var products = await _context.Products.ToListAsync();
            var result = new List<ProductDto>();

            foreach (var product in products)
            {
                var user = await _userManager.FindByIdAsync(product.CreatedByUserId.ToString());
                result.Add(MapToDto(product, user?.Email ?? "Unknown"));
            }
            return result;
        }

        public async Task<ProductDto> UpdateProductAsync(Guid id, CreateProductDto dto, Guid userId)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product == null)
                throw new Exception("Product không tìm thấy");

            if (product.CreatedByUserId != userId)
                throw new Exception("Bạn không có quyền sửa product này");

            product.Name = dto.Name;
            product.Description = dto.Description;
            product.Price = dto.Price;
            product.Stock = dto.Stock;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var user = await _userManager.FindByIdAsync(userId.ToString());
            return MapToDto(product, user?.Email ?? "Unknown");
        }

        public async Task DeleteProductAsync(Guid id, Guid userId)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product == null)
                throw new Exception("Product không tìm thấy");

            if (product.CreatedByUserId != userId)
                throw new Exception("Bạn không có quyền xóa product này");

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
        }

        private ProductDto MapToDto(Product product, string createdByEmail)
        {
            return new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock,
                CreatedAt = product.CreatedAt,
                CreatedByEmail = createdByEmail
            };
        }
    }
}
