using App.Application.DTOs;
using App.Application.Interfaces;
using App.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using App.Domain.Constants;


namespace App.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public class ProductController : ControllerBase
    {
        private readonly IProductService _productService;
        private readonly UserManager<User> _userManager;

        public ProductController(IProductService productService, UserManager<User> userManager)
        {
            _productService = productService;
            _userManager = userManager;
        }

        private Guid GetCurrentUserId()
        {
            var userId = User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
            return Guid.Parse(userId!);
        }

        [HttpPost]
        // [Authorize(Roles = "Admin,Manager")]
        [Authorize(Policy = "ProductCreate")]
        public async Task<IActionResult> CreateProduct([FromBody] CreateProductDto dto)
        {
            var userId = GetCurrentUserId();
            var product = await _productService.CreateProductAsync(dto, userId);
            return CreatedAtAction(nameof(GetProductById), new { id = product.Id }, product);
        }

        [HttpGet("{id}")]
        // [Authorize(Roles = "Admin,Manager,Employee,Viewer")]
        [Authorize(Policy = "ProductRead")]
        public async Task<IActionResult> GetProductById(Guid id)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();
            return Ok(product);
        }

        [HttpGet]
        // [Authorize(Roles = "Admin,Manager,Employee,Viewer")]
        [Authorize(Policy = "ProductRead")]
        public async Task<IActionResult> GetAllProducts()
        {
            var products = await _productService.GetAllProductsAsync();
            return Ok(products);
        }

        [HttpPut("{id}")]
        // [Authorize(Roles = "Admin,Manager,Employee")]
        [Authorize(Policy = "ProductUpdate")]
        public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] CreateProductDto dto)
        {
            var userId = GetCurrentUserId();
            var product = await _productService.UpdateProductAsync(id, dto, userId);
            return Ok(product);
        }

        [HttpDelete("{id}")]
        // [Authorize(Roles = "Admin,Manager")]
        [Authorize(Policy = "ProductDelete")]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var userId = GetCurrentUserId();
            await _productService.DeleteProductAsync(id, userId);
            return NoContent();
        }
    }
}
