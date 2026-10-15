using System.Text.Json.Serialization;
using VanderBeer.Contracts.Operations;
using VanderBeer.Application.Catalog;
using VanderBeer.Application.Customers;
using VanderBeer.Application.Operations;
using VanderBeer.Application.Orders;
using VanderBeer.Infrastructure.Catalog;
using VanderBeer.Infrastructure.Customers;
using VanderBeer.Infrastructure.Persistence;
using VanderBeer.Infrastructure.Orders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.JsonSerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .SelectMany(field => field.Value?.Errors.Select(error =>
                new FieldErrorDto(field.Key, string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "Valor inválido." : error.ErrorMessage)) ?? [])
            .Take(200).ToArray();
        var problem = ApiProblemDto.Create(400, "ValidationFailed", "Revise os campos.",
            context.HttpContext.TraceIdentifier, errors);
        var result = new Microsoft.AspNetCore.Mvc.ObjectResult(problem) { StatusCode = 400 };
        result.ContentTypes.Add("application/problem+json");
        return result;
    };
});
builder.Services.AddOpenApi();
builder.Services.AddSingleton(new MySqlConnectionFactory(
    builder.Configuration["VANDERBEER_DB_CONNECTION"]));
builder.Services.AddScoped<IDatabaseProbe, MySqlDatabaseProbe>();
builder.Services.AddScoped<ICategoryReader, MySqlCategoryReader>();
builder.Services.AddScoped<IProductCatalog, MySqlProductCatalog>();
builder.Services.AddScoped<ICustomerRegistry, MySqlCustomerRegistry>();
builder.Services.AddScoped<IOrderRegistry, MySqlOrderRegistry>();
builder.Services.AddSingleton<IOrderClock, MySqlOrderClock>();

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    var problem = ApiProblemDto.Create(500, "InternalError", "Tente novamente mais tarde.",
        context.TraceIdentifier);
    await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, problem,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
        context.RequestAborted);
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.Run();

public partial class Program;
