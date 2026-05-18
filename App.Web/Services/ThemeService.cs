using Microsoft.JSInterop;

namespace App.Web.Services;

public class ThemeService
{
    private readonly IJSRuntime _js;
    public string Current { get; private set; } = "light";
    public event Action? OnChange;

    public ThemeService(IJSRuntime js) => _js = js;

    public async Task InitAsync()
    {
        Current = await _js.InvokeAsync<string>("getTheme");
        OnChange?.Invoke();
    }

    public async Task ToggleAsync()
    {
        Current = Current == "dark" ? "light" : "dark";
        await _js.InvokeVoidAsync("applyTheme", Current);
        OnChange?.Invoke();
    }
}
