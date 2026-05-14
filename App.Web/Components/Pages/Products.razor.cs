using App.Web.Services;
using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace App.Web.Components.Pages;

public partial class Products
{
    [Inject] private ApiClient ApiClient { get; set; } = default!;

    [SupplyParameterFromForm(FormName = "create-product")]
    private CreateProductInput? NewProduct { get; set; }

    [SupplyParameterFromForm(FormName = "delete-product")]
    private DeleteProductInput? DeleteTarget { get; set; }

    private List<ProductItem>? _products;
    private string? _error;
    private string? _success;

    protected override async Task OnInitializedAsync()
    {
        if (NewProduct is not null)
        {
            var body = new
            {
                name = NewProduct.Name,
                price = NewProduct.Price,
                description = NewProduct.Description,
                stock = NewProduct.Stock
            };
            var response = await ApiClient.CallApiAsync(HttpMethod.Post, "/api/Product", body);
            if (response.IsSuccessStatusCode)
                _success = "Thêm sản phẩm thành công!";
            else
                _error = $"Lỗi thêm sản phẩm: {(int)response.StatusCode}";
        }

        if (DeleteTarget is not null)
        {
            var response = await ApiClient.CallApiAsync(HttpMethod.Delete, $"/api/Product/{DeleteTarget.Id}");
            if (response.IsSuccessStatusCode)
                _success = "Xóa sản phẩm thành công!";
            else
                _error = $"Lỗi xóa sản phẩm: {(int)response.StatusCode}";
        }

        var getResponse = await ApiClient.CallApiAsync(HttpMethod.Get, "/api/Product");
        if (getResponse.IsSuccessStatusCode)
        {
            var json = await getResponse.Content.ReadAsStringAsync();
            _products = JsonSerializer.Deserialize<List<ProductItem>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        else
        {
            _error ??= "Không thể tải danh sách sản phẩm.";
        }
    }

    private record ProductItem(
        Guid Id, string Name, string? Description,
        decimal Price, int Stock, string CreatedByEmail, DateTime CreatedAt);

    private class CreateProductInput
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }

    private class DeleteProductInput
    {
        public Guid Id { get; set; }
    }
}
