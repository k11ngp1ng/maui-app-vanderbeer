using Microsoft.AspNetCore.Mvc;
using VanderBeer.Application.Operations;
using VanderBeer.Application.Orders;
using VanderBeer.Contracts.Operations;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController(IOrderRegistry orders) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) return ProductHttpResults.Problem(HttpContext,
            ApiProblemDto.Create(400, "ValidationFailed", "Informe um ID válido.", HttpContext.TraceIdentifier));
        try
        {
            var order = await orders.GetAsync(id, token);
            return order is null ? ProductHttpResults.Problem(HttpContext,
                ApiProblemDto.Create(404, "NotFound", "Pedido não encontrado.", HttpContext.TraceIdentifier))
                : Ok(order);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken token)
    {
        var errors = OrderValidation.ValidateStructure(OrderValidation.Normalize(request));
        if (errors.Count > 0) return ProductHttpResults.Problem(HttpContext,
            ApiProblemDto.Create(400, "ValidationFailed", "Revise os campos.",
                HttpContext.TraceIdentifier, errors));
        try
        {
            var result = await orders.CreateAsync(request, HttpContext.TraceIdentifier, token);
            if (result.Problem?.Code != "RequestKeyConflict")
                Response.Headers["Idempotent-Replay"] = result.Replay ? "true" : "false";
            if (result.Status == 201 && result.Order is not null)
                Response.Headers.Location = $"/api/v1/orders/{result.Order.Id:D}";
            return result.Order is not null
                ? new ObjectResult(result.Order) { StatusCode = result.Status }
                : ProductHttpResults.Problem(HttpContext, result.Problem!);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }
}

[ApiController]
[Route("api/v1/admin/orders")]
public sealed class CompanyOrdersController(IOrderRegistry orders) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null, CancellationToken token = default)
    {
        List<FieldErrorDto> errors = [];
        if (page < 1) errors.Add(new("page", "Informe uma página positiva."));
        if (pageSize is < 1 or > 100) errors.Add(new("pageSize", "Informe de 1 a 100 registros."));
        if (status is not null && !OrderStatusRules.IsStatus(status))
            errors.Add(new("status", "Informe um estado válido."));
        if (fromDate > toDate) errors.Add(new("toDate", "A data final deve ser igual ou posterior à inicial."));
        if (fromDate == DateOnly.MaxValue) errors.Add(new("fromDate", "Data inicial fora do intervalo permitido."));
        if (toDate == DateOnly.MaxValue) errors.Add(new("toDate", "Data final fora do intervalo permitido."));
        if (errors.Count > 0) return Validation(errors);
        try { return Ok(await orders.ListCompanyAsync(page, pageSize, status, fromDate, toDate, token)); }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) return Validation([new("id", "Informe um ID válido.")]);
        try
        {
            var order = await orders.GetAsync(id, token);
            return order is null ? ProductHttpResults.Problem(HttpContext,
                ApiProblemDto.Create(404, "NotFound", "Pedido não encontrado.", HttpContext.TraceIdentifier))
                : Ok(order);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request,
        CancellationToken token)
    {
        var errors = OrderStatusRules.ValidateStructure(OrderStatusRules.Normalize(request)).ToList();
        if (id == Guid.Empty) errors.Add(new("id", "Informe um ID válido."));
        if (errors.Count > 0) return Validation(errors);
        try
        {
            var result = await orders.UpdateStatusAsync(id, request, HttpContext.TraceIdentifier, token);
            if (result.Problem?.Code != "RequestKeyConflict")
                Response.Headers["Idempotent-Replay"] = result.Replay ? "true" : "false";
            return result.Order is not null
                ? new ObjectResult(result.Order) { StatusCode = result.Status }
                : ProductHttpResults.Problem(HttpContext, result.Problem!);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    private ObjectResult Validation(IReadOnlyList<FieldErrorDto> errors) => ProductHttpResults.Problem(
        HttpContext, ApiProblemDto.Create(400, "ValidationFailed", "Revise os campos.",
            HttpContext.TraceIdentifier, errors));
}
