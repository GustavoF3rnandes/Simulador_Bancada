using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Simulador1.Client;
using Simulador1.Client.Services;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped<Simulador1.Client.Services.BancadaState>();
builder.Services.AddScoped<Simulador1.Client.Services.ControladoraState>();

builder.Services.AddRadzenComponents();

await builder.Build().RunAsync();
