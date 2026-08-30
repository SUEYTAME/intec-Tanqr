using System.ComponentModel.DataAnnotations;

namespace Combustible.Api.Endpoints;

public sealed class RequestValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is null || argument.GetType().Namespace != "Combustible.Application") continue;
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(argument, new ValidationContext(argument), results, true))
                return Results.ValidationProblem(results.GroupBy(x => x.MemberNames.FirstOrDefault() ?? "request")
                    .ToDictionary(x => x.Key, x => x.Select(r => r.ErrorMessage ?? "Valor inválido").ToArray()));
        }
        return await next(context);
    }
}
