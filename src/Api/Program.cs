using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Api.Data;
using Api.Features.Leads;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------------------- services

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddScoped<AdminKeyFilter>();

// Without this the validation attributes on the request records are inert — minimal APIs
// do not check them by default. It registers a filter that runs before each handler and
// returns 400 with the field errors, so no handler ever sees a malformed request.
builder.Services.AddValidation();

// Enums travel as their names, both directions. Without this LeadStatus arrives and leaves
// as a bare integer: "status": 2 tells a client nothing, and posting "Replied" fails to
// deserialise. Names also mean adding a status later cannot silently renumber the others.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// The site is served from a different origin than the API, so the browser needs
// permission to post to it. Origins come from configuration, never a wildcard:
// a wildcard would let any page on the internet submit this form.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:Origins").Get<string[]>() ?? [];

// GET and PATCH are here for the admin page, which is served from the same origin as the
// site. They are not a hole: every admin route still requires the key, and CORS only
// decides which origins a browser may call from.
builder.Services.AddCors(options =>
    options.AddPolicy("site", policy => policy
        .WithOrigins(allowedOrigins)
        .WithMethods("GET", "POST", "PATCH")
        .WithHeaders("Content-Type", "X-Admin-Key")));

// Five attempts a minute per address. The limiter sits in front of validation, so a
// rejected-as-invalid attempt still costs a permit — someone who mistypes their address
// and corrects it needs several. Five leaves room for that and still stops volume.
// Genuine repeat submissions are caught separately by the duplicate window.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("contact-form", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Behind a reverse proxy the socket address is the proxy's. Without this the rate
// limiter would bucket every visitor together under one address.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

// ----------------------------------------------------------------- pipeline

app.UseForwardedHeaders();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;

    // A binding failure — an unparseable enum in the query string, malformed JSON — is the
    // caller's mistake and carries its own 4xx code. Reporting it as 500 tells the caller
    // the server is broken and buries a fixable request in the error log.
    if (error is BadHttpRequestException badRequest)
    {
        app.Logger.LogInformation(
            "Rejected {Path}: {Reason}", context.Request.Path, badRequest.Message);

        await Results.Problem(
            title: "Invalid request",
            detail: badRequest.Message,
            statusCode: badRequest.StatusCode)
            .ExecuteAsync(context);

        return;
    }

    app.Logger.LogError(error, "Unhandled exception on {Path}.", context.Request.Path);

    await Results.Problem(
        title: "Something went wrong",
        detail: "The request could not be completed. Please try again.",
        statusCode: StatusCodes.Status500InternalServerError)
        .ExecuteAsync(context);
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // Only outside development: locally the app listens on HTTP alone, and the redirect
    // middleware then logs "failed to determine the https port" on every single request.
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors("site");
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapLeadEndpoints();

app.Run();
