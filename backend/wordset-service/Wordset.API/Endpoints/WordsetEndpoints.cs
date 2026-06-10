using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Wordset.Application.Models;
using Wordset.Application.Services;
using Wordset.Domain.Exceptions;

namespace Wordset.API.Endpoints;

public static class WordsetEndpoints
{
    public static IEndpointRouteBuilder MapWordsetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wordsets").WithTags("Wordsets");

        group.MapGet("/", GetWordsets)
            .WithSummary("List wordsets (paginated)")
            .WithDescription(
                "Returns a cursor-paginated page of wordsets, sorted alphabetically by share code. " +
                "Pass the nextCursor from a previous response to retrieve the next page. " +
                "A null nextCursor means you have reached the last page.");

        group.MapGet("/{shareCode}", GetWordsetById)
            .WithSummary("Get a wordset")
            .WithDescription("Returns a single wordset by its share code.");

        group.MapPost("/", CreateWordset)
            .WithSummary("Create a wordset")
            .WithDescription("Creates a new empty wordset and returns it with a generated share code. Add words via PUT /wordsets/{shareCode}/words.");

        group.MapPatch("/{shareCode}", UpdateWordset)
            .WithSummary("Rename a wordset")
            .WithDescription("Updates the display name of an existing wordset. Share code and word list are unchanged.");

        return app;
    }

    private static async Task<Results<Ok<WordsetPageDto>, ValidationProblem>> GetWordsets(
        [Description("Opaque pagination cursor from a previous response. Omit for the first page.")] string? cursor,
        [Description("Number of wordsets per page.")] int? pageSize,
        WordsetService service,
        CancellationToken ct)
    {
        try
        {
            var page = await service.GetWordsetPageAsync(cursor, pageSize, ct);
            return TypedResults.Ok(page);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "pageSize")
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageSize"] = [$"pageSize must be between 1 and {WordsetService.MaxWordsetPageSize}."]
            });
        }
        catch (ArgumentException ex) when (ex.ParamName == "cursor")
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["cursor"] = ["Invalid cursor value."]
            });
        }
    }

    private static async Task<Results<Ok<WordsetDto>, NotFound>> GetWordsetById(
        string shareCode,
        WordsetService service,
        CancellationToken ct)
    {
        try
        {
            var wordset = await service.GetByShareCodeAsync(shareCode, ct);
            return TypedResults.Ok(wordset);
        }
        catch (WordsetNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    private static async Task<Results<Created<WordsetDto>, ValidationProblem>> CreateWordset(
        CreateWordsetRequest request,
        WordsetService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Name)] = ["Name is required."]
            });

        var wordset = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/wordsets/{wordset.ShareCode}", wordset);
    }

    private static async Task<Results<Ok<WordsetDto>, NotFound, ValidationProblem>> UpdateWordset(
        string shareCode,
        UpdateWordsetRequest request,
        WordsetService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Name)] = ["Name is required."]
            });

        try
        {
            var wordset = await service.UpdateAsync(shareCode, request, ct);
            return TypedResults.Ok(wordset);
        }
        catch (WordsetNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }
}
