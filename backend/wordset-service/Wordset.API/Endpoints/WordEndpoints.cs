using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Wordset.Application.Models;
using Wordset.Application.Services;
using Wordset.Domain.Exceptions;

namespace Wordset.API.Endpoints;

public static class WordEndpoints
{
    public static IEndpointRouteBuilder MapWordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wordsets/{shareCode}/words").WithTags("Words");

        group.MapGet("/", GetWords)
            .WithSummary("Get words (paginated)")
            .WithDescription(
                "Returns a cursor-paginated page of words for a wordset, sorted alphabetically. " +
                "Pass the nextCursor from a previous response to retrieve the next page. " +
                "A null nextCursor means you have reached the last page.");

        group.MapPut("/", ReplaceWords)
            .WithSummary("Replace the word list")
            .WithDescription(
                "Atomically replaces the entire word list. Words are normalised to lowercase and deduplicated. " +
                "Existing frequency_rank values are preserved for words that survive the replacement; " +
                "new words receive frequency_rank = 0. Designed for large batches (thousands of words).");

        return app;
    }

    private static async Task<Results<Ok<WordsPageDto>, NotFound, ValidationProblem>> GetWords(
        string shareCode,
        [Description("Opaque pagination cursor from a previous response. Omit for the first page.")] string? cursor,
        [Description("Number of words per page.")] int? pageSize,
        WordService service,
        CancellationToken ct)
    {
        try
        {
            var page = await service.GetWordsAsync(shareCode, cursor, pageSize, ct);
            return TypedResults.Ok(page);
        }
        catch (WordsetNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "pageSize")
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageSize"] = [$"pageSize must be between 1 and {WordService.MaxPageSize}."]
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

    private static async Task<Results<Ok<WordsetDto>, NotFound, ValidationProblem>> ReplaceWords(
        string shareCode,
        ReplaceWordsRequest request,
        WordService service,
        CancellationToken ct)
    {
        if (request.Words is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Words)] = ["Words list is required."]
            });

        try
        {
            var wordset = await service.ReplaceWordsAsync(shareCode, request, ct);
            return TypedResults.Ok(wordset);
        }
        catch (WordsetNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }
}
