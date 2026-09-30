using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Legacy.Maliev.CountryService.Api.Documentation;

internal static class CountryOpenApi
{
    internal static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Normal JWT bearer authentication; the operation's permission is also required.",
            };
            foreach (var path in document.Paths.Values)
            {
                if (path.Operations is null) continue;
                foreach (var operation in path.Operations.Values)
                {
                    // The operation transformer marks only genuinely protected routes.
                    if (operation.Security is not null && operation.Security.Count > 0)
                        operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                }
            }
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, cancellationToken) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (!metadata.OfType<IAllowAnonymous>().Any() && metadata.OfType<IAuthorizeData>().Any())
                operation.Security = [new OpenApiSecurityRequirement()];
            return Task.CompletedTask;
        });
        options.AddSchemaTransformer((schema, context, cancellationToken) =>
        {
            if (context.JsonTypeInfo.Type != typeof(UpsertCountryRequest) || schema.Properties is null)
                return Task.CompletedTask;
            var required = new HashSet<string>();
            var constructor = typeof(UpsertCountryRequest).GetConstructors().Single();
            foreach (var parameter in constructor.GetParameters())
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!);
                var length = parameter.GetCustomAttribute<StringLengthAttribute>();
                if (schema.Properties.TryGetValue(name, out var property) && property is OpenApiSchema concrete)
                    concrete.MaxLength = length?.MaximumLength;
                if (parameter.GetCustomAttribute<RequiredAttribute>() is not null)
                    required.Add(name);
            }
            schema.Required = required;
            return Task.CompletedTask;
        });
    }
}
