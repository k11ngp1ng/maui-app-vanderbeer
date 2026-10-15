using Microsoft.AspNetCore.Mvc;
using VanderBeer.Application.Customers;
using VanderBeer.Application.Operations;
using VanderBeer.Application.Orders;
using VanderBeer.Contracts.Customers;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerRegistry customers, IOrderRegistry orders) : ControllerBase
{
    [HttpGet("{id:guid}/orders")]
    public async Task<IActionResult> ListOrders(Guid id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        List<FieldErrorDto> errors = [];
        if (id == Guid.Empty) errors.Add(new("id", "Informe um ID válido."));
        if (page < 1) errors.Add(new("page", "Informe uma página positiva."));
        if (pageSize is < 1 or > 100) errors.Add(new("pageSize", "Informe de 1 a 100 itens por página."));
        if (errors.Count > 0) return ProblemResult(400, "ValidationFailed", "Revise os filtros.", errors);
        try
        {
            var result = await orders.ListCustomerAsync(id, page, pageSize, token);
            return result is null ? ProblemResult(404, "NotFound", "Cliente não encontrado.") : Ok(result);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) return ProblemResult(400, "ValidationFailed", "Informe um ID válido.");
        try
        {
            var customer = await customers.GetAsync(id, token);
            return customer is null ? ProblemResult(404, "NotFound", "Cliente não encontrado.") : Ok(customer);
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerWriteRequest request, CancellationToken token)
    {
        var errors = CustomerValidation.Validate(CustomerValidation.Normalize(request));
        if (errors.Count > 0) return ProblemResult(400, "ValidationFailed", "Revise os campos.", errors);
        try
        {
            var result = await customers.CreateAsync(request, HttpContext.TraceIdentifier, token);
            return CommandResult(result, result.Customer is null ? null : $"/api/v1/customers/{result.Customer.Id:D}");
        }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CustomerUpdateRequest request, CancellationToken token)
    {
        var value = CustomerValidation.Normalize(new(request.ClientRequestId, request.CustomerType,
            request.FullName, request.CompanyName, request.Cpf, request.Cnpj, request.Phone, request.Email,
            request.Address));
        var errors = CustomerValidation.Validate(value, request.ExpectedVersion).ToList();
        if (id == Guid.Empty) errors.Add(new("id", "Informe um ID válido."));
        if (errors.Count > 0) return ProblemResult(400, "ValidationFailed", "Revise os campos.", errors);
        try { return CommandResult(await customers.UpdateAsync(id, request, HttpContext.TraceIdentifier, token)); }
        catch (DataStoreUnavailableException) { return ErrorResponses.ServiceUnavailable(HttpContext); }
    }

    private IActionResult CommandResult(CustomerCommandResult result, string? location = null)
    {
        if (result.Problem?.Code != "RequestKeyConflict")
            Response.Headers["Idempotent-Replay"] = result.Replay ? "true" : "false";
        if (result.Status == 201 && location is not null) Response.Headers.Location = location;
        return result.Customer is not null ? new ObjectResult(result.Customer) { StatusCode = result.Status }
            : ProductHttpResults.Problem(HttpContext, result.Problem!);
    }

    private ObjectResult ProblemResult(int status, string code, string detail,
        IReadOnlyList<FieldErrorDto>? errors = null) => ProductHttpResults.Problem(HttpContext,
        ApiProblemDto.Create(status, code, detail, HttpContext.TraceIdentifier, errors));
}
