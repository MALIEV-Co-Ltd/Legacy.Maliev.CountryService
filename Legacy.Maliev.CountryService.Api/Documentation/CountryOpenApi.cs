using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Controllers;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

            // This native Int32 path identity is not a JSON body number-coercion contract.
            if (context.Description.ActionDescriptor is ControllerActionDescriptor action
                && action.ControllerTypeInfo.AsType() == typeof(CountriesController)
                && ((context.Description.HttpMethod == "GET" && action.MethodInfo.Name == nameof(CountriesController.GetCountryAsync))
                    || (context.Description.HttpMethod == "PUT" && action.MethodInfo.Name == nameof(CountriesController.UpdateCountryAsync))
                    || (context.Description.HttpMethod == "DELETE" && action.MethodInfo.Name == nameof(CountriesController.DeleteCountryAsync)))
                && context.Description.ParameterDescriptions.Any(parameter => parameter.Name == "id"
                    && parameter.Source == BindingSource.Path
                    && parameter.ParameterDescriptor is ControllerParameterDescriptor native
                    && native.ParameterInfo.Name == "id" && native.ParameterInfo.ParameterType == typeof(int)
                    && native.ParameterInfo.Member.Equals(action.MethodInfo))
                && operation.Parameters is not null)
            {
                foreach (var parameter in operation.Parameters)
                {
                    if (parameter.Name == "id" && parameter.In == ParameterLocation.Path
                        && parameter.Schema is OpenApiSchema identity)
                    {
                        identity.Type = JsonSchemaType.Integer;
                        identity.Format = "int32";
                    }
                }
            }
            return Task.CompletedTask;
        });
        options.AddSchemaTransformer((schema, context, cancellationToken) =>
        {
            // Output identity is always a JSON integer; do not inherit input number coercion.
            if (context.JsonTypeInfo.Type == typeof(CountryResponse) && schema.Properties is not null
                && schema.Properties.TryGetValue("id", out var identity) && identity is OpenApiSchema countryId)
            {
                countryId.Type = JsonSchemaType.Integer;
                countryId.Format = "int32";
            }
            if (context.JsonTypeInfo.Type != typeof(UpsertCountryRequest) || schema.Properties is null)
                return Task.CompletedTask;
            // Setter-only identity/audit checks are not public DTO fields.
            foreach (var discarded in new[] { "id", "createdDate", "modifiedDate" })
                schema.Properties.Remove(discarded);
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
