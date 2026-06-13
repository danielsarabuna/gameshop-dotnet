using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Web;
using Shopping.Web.Services;
using Shopping.Web.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseConfigured = builder.Configuration["ApiBaseUrl"]?.Trim();
var apiBase = !string.IsNullOrWhiteSpace(apiBaseConfigured)
    ? apiBaseConfigured.TrimEnd('/') + "/"
    : builder.HostEnvironment.BaseAddress;

var oidcAuthority = builder.Configuration["OIDC:Authority"]?.Trim();
var oidcEnabled = !string.IsNullOrWhiteSpace(oidcAuthority);

if (oidcEnabled)
{
    builder.Services.AddOidcAuthentication(options =>
    {
        builder.Configuration.Bind("OIDC", options.ProviderOptions);
        if (string.IsNullOrWhiteSpace(options.ProviderOptions.ResponseType))
        {
            options.ProviderOptions.ResponseType = "code";
        }
    });

    builder.Services.AddHttpClient("WebShop.Api", client => client.BaseAddress = new Uri(apiBase))
        .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
            .ConfigureHandler(authorizedUrls: new[] { apiBase }));

    builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("WebShop.Api"));

    builder.Services.AddHttpClient<PaymentService>("WebShop.Api.Payments", client => client.BaseAddress = new Uri(apiBase))
        .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
            .ConfigureHandler(authorizedUrls: new[] { apiBase }));
}
else
{
    builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBase) });
    builder.Services.AddHttpClient<PaymentService>(client => client.BaseAddress = new Uri(apiBase));
}

builder.Services.AddSingleton<CartState>();
builder.Services.AddSingleton<LanguageState>();

await builder.Build().RunAsync();
