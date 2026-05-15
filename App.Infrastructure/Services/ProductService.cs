using App.Application.DTOs;
using App.Application.Interfaces;
using App.Domain.Entities;
using App.Domain.Exceptions;
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
        private readonly AuditLogService _auditLog;

        public ProductService(
            AppDbContext context,
            UserManager<User> userManager,
            ITenantContext tenantContext,
            AuditLogService auditLog)
        {
            _context = context;
            _userManager = userManager;
            _tenantContext = tenantContext;
            _auditLog = auditLog;
        }

        public async Task<ProductDto> CreateProductAsync(CreateProductDto dto, Guid userId)
        {
            if (!_tenantContext.TenantId.HasValue)
                throw new InvalidOperationException("Tenant context is required to create a product.");

            var product = new Product
            {
                TenantId = _tenantContext.TenantId.Value,
                Name = dto.Name,
                Description = dto.Description ?? string.Empty,
                Price = dto.Price,
                Stock = dto.Stock,
                CreatedByUserId = userId
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(AuditEventTypes.ProductCreated, _tenantContext.TenantId, userId,
                resourceType: "Product", resourceId: product.Id.ToString(),
                newValue: product.Name);

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

        public async Task<PagedResult<ProductDto>> GetAllProductsAsync(int page = 1, int pageSize = 20)
        {
            var query = _context.Products.AsNoTracking().OrderByDescending(p => p.CreatedAt);
            var total = await query.CountAsync();

            var products = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userIds = products.Select(p => p.CreatedByUserId.ToString()).Distinct().ToList();
            var users = new Dictionary<string, string>();
            foreach (var uid in userIds)
            {
                var user = await _userManager.FindByIdAsync(uid);
                if (user?.Email is not null)
                    users[uid] = user.Email;
            }

            return new PagedResult<ProductDto>
            {
                Total = total,
                Page = page,
                PageSize = pageSize,
                Items = products
                    .Select(p => MapToDto(p, users.GetValueOrDefault(p.CreatedByUserId.ToString(), "Unknown")))
                    .ToList()
            };
        }

        public async Task<ProductDto> UpdateProductAsync(Guid id, CreateProductDto dto, Guid userId)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product == null)
                throw new NotFoundException("Product not found.");

            if (product.CreatedByUserId != userId)
                throw new ForbiddenException("You do not have permission to update this product.");

            var oldName = product.Name;
            product.Name = dto.Name;
            product.Description = dto.Description ?? string.Empty;
            product.Price = dto.Price;
            product.Stock = dto.Stock;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(AuditEventTypes.ProductUpdated, _tenantContext.TenantId, userId,
                resourceType: "Product", resourceId: product.Id.ToString(),
                oldValue: oldName, newValue: product.Name);

            var user = await _userManager.FindByIdAsync(userId.ToString());
            return MapToDto(product, user?.Email ?? "Unknown");
        }

        public async Task DeleteProductAsync(Guid id, Guid userId)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product == null)
                throw new NotFoundException("Product not found.");

            if (product.CreatedByUserId != userId)
                throw new ForbiddenException("You do not have permission to delete this product.");

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(AuditEventTypes.ProductDeleted, _tenantContext.TenantId, userId,
                resourceType: "Product", resourceId: id.ToString(),
                oldValue: product.Name);
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
