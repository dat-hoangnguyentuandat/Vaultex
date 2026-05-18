using Microsoft.AspNetCore.Hosting;
using Microsoft.JSInterop;
using System.Text.Json;

namespace App.Web.Services;

public class I18nService
{
    private readonly IJSRuntime _js;
    private readonly IWebHostEnvironment _env;
    private Dictionary<string, string> _strings = new();
    public string CurrentLang { get; private set; } = "en";
    public event Action? OnChange;

    private static readonly string[] Supported = ["en", "vi", "zh", "ja"];

    public I18nService(IJSRuntime js, IWebHostEnvironment env)
    {
        _js = js;
        _env = env;
        LoadSync("vi");
    }

    private void LoadSync(string lang)
    {
        try
        {
            var path = Path.Combine(_env.WebRootPath, "i18n", $"{lang}.json");
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            CurrentLang = lang;
        }
        catch { _strings = new(); }
    }

    public async Task InitAsync()
    {
        var browserLang = await _js.InvokeAsync<string>("getLanguage");
        CurrentLang = Supported.Contains(browserLang) ? browserLang : "en";
        await LoadAsync(CurrentLang);
        OnChange?.Invoke();
    }

    public async Task SetLanguageAsync(string lang)
    {
        CurrentLang = lang;
        await _js.InvokeVoidAsync("setLanguage", lang);
        await LoadAsync(lang);
        OnChange?.Invoke();
    }

    public string T(string key) => _strings.TryGetValue(key, out var v) ? v : key;

    private async Task LoadAsync(string lang)
    {
        try
        {
            var path = Path.Combine(_env.WebRootPath, "i18n", $"{lang}.json");
            var json = await File.ReadAllTextAsync(path);
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            _strings = new();
        }
    }
}
