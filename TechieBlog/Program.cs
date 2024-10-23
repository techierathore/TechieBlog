using Blazored.LocalStorage;
using Blazorise;
using Blazorise.Bootstrap;
using Blazorise.Icons.FontAwesome;
using BlogEngine;
using BlogModels.Interfaces;
using BlogUI;
using Humanizer.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechieBlog.Services;


var builder = WebApplication.CreateBuilder(args);

builder.Services
  .AddBlazorise(options =>
  {
      options.Immediate = true; // optional
  })
  .AddBootstrapProviders()
  .AddFontAwesomeIcons();

string sDbConnectionString = builder.Configuration["AppDbConString"];

// Initialize ItsmCore Services 
BlogSvcInitializer.Initialize(builder.Services, sDbConnectionString);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
        .AddHubOptions(o => { o.MaximumReceiveMessageSize = 10 * 1024 * 1024; })
        .AddCircuitOptions(options => { options.DetailedErrors = true; });



builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddTransient<IAuthService, AuthService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
