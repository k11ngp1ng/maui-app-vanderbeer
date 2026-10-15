using Microsoft.AspNetCore.Mvc;
using VanderBeer.Application.Catalog;
using VanderBeer.Application.Operations;
using VanderBeer.Contracts.Catalog;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public sealed class ProductsController(IProductCatalog catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] Guid? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new ProductQuery(page, pageSize, search, categoryId, null);
        var errors = ProductValidation.ValidateQuery(query);
        if (errors.Count > 0) return ProblemResult(400, "ValidationFailed", "Revise os filtros.", errors);
        try { return Ok(await catalog.ListAsync(query, false, cancellationToken)); }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return ProblemResult(400, "ValidationFailed", "Informe um ID válido.");
        try
        {
            var product = await catalog.GetAsync(id, false, cancellationToken);
            return product is null ? ProblemResult(404, "NotFound", "Produto não encontrado.") : Ok(product);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    private ObjectResult ProblemResult(int status, string code, string detail,
        IReadOnlyList<FieldErrorDto>? errors = null) => ProductHttpResults.Problem(HttpContext,
        ApiProblemDto.Create(status, code, detail, HttpContext.TraceIdentifier, errors));
}

[ApiController]
[Route("api/v1/payment-methods")]
public sealed class PaymentMethodsController(IProductCatalog catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try { return Ok(await catalog.GetPaymentMethodsAsync(cancellationToken)); }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }
}

[ApiController]
[Route("api/v1/admin/products")]
public sealed class CompanyProductsController(IProductCatalog catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] Guid? categoryId = null,
        [FromQuery] bool? isActive = null, CancellationToken cancellationToken = default)
    {
        var query = new ProductQuery(page, pageSize, search, categoryId, isActive);
        var errors = ProductValidation.ValidateQuery(query);
        if (errors.Count > 0) return ProductHttpResults.Problem(HttpContext,
            ApiProblemDto.Create(400, "ValidationFailed", "Revise os filtros.", HttpContext.TraceIdentifier, errors));
        try { return Ok(await catalog.ListAsync(query, true, cancellationToken)); }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return NotFoundProblem();
        try
        {
            var product = await catalog.GetAsync(id, true, cancellationToken);
            return product is null ? NotFoundProblem() : Ok(product);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductWriteRequest request, CancellationToken cancellationToken)
    {
        var errors = ProductValidation.Validate(request);
        if (errors.Count > 0) return ValidationProblem(errors);
        try
        {
            var result = await catalog.CreateAsync(request, HttpContext.TraceIdentifier, cancellationToken);
            return ProductHttpResults.Command(HttpContext, result, result.Product is null ? null :
                $"/api/v1/admin/products/{result.Product.Id:D}");
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProductUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var value = new ProductWriteRequest(request.ClientRequestId, request.CategoryId, request.Sku,
            request.Name, request.Description, request.UnitLabel, request.Price, request.ImageUrl,
            request.IsAvailable, request.IsActive);
        var errors = ProductValidation.Validate(value, request.ExpectedVersion).ToList();
        if (id == Guid.Empty) errors.Add(new("id", "Informe um ID válido."));
        if (errors.Count > 0) return ValidationProblem(errors);
        try
        {
            var result = await catalog.UpdateAsync(id, request, HttpContext.TraceIdentifier, cancellationToken);
            return ProductHttpResults.Command(HttpContext, result);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    private ObjectResult ValidationProblem(IReadOnlyList<FieldErrorDto> errors) => ProductHttpResults.Problem(HttpContext,
        ApiProblemDto.Create(400, "ValidationFailed", "Revise os campos.", HttpContext.TraceIdentifier, errors));
    private ObjectResult NotFoundProblem() => ProductHttpResults.Problem(HttpContext,
        ApiProblemDto.Create(404, "NotFound", "Produto não encontrado.", HttpContext.TraceIdentifier));
}

internal static class ProductHttpResults
{
    public static ObjectResult Problem(HttpContext context, ApiProblemDto problem)
    {
        var result = new ObjectResult(problem) { StatusCode = problem.Status };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    public static IActionResult Command(HttpContext context, ProductCommandResult result, string? location = null)
    {
        if (result.Problem?.Code != "RequestKeyConflict")
            context.Response.Headers["Idempotent-Replay"] = result.Replay ? "true" : "false";
        if (result.Status == 201 && location is not null) context.Response.Headers.Location = location;
        return result.Product is not null
            ? new ObjectResult(result.Product) { StatusCode = result.Status }
            : Problem(context, result.Problem!);
    }
}
