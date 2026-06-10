using Grpc.Core;
using Wordset.API.Protos;
using Wordset.Application.Services;
using Wordset.Domain.Exceptions;

namespace Wordset.API.GrpcServices;

public class WordsetGameGrpcService(WordGameService wordGameService) : WordsetGameService.WordsetGameServiceBase
{
    public override async Task<GetChallengeResponse> GetChallenge(
        GetChallengeRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ShareCode))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "share_code is required."));
        if (request.LetterCount < 1)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "letter_count must be at least 1."));

        try
        {
            var result = await wordGameService.GetChallengeAsync(
                request.ShareCode,
                request.LetterCount,
                context.CancellationToken);

            var response = new GetChallengeResponse();
            response.Letters.AddRange(result.Letters);
            return response;
        }
        catch (WordsetNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Wordset '{request.ShareCode}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    public override async Task<ValidateWordResponse> ValidateWord(
        ValidateWordRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ShareCode))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "share_code is required."));
        if (string.IsNullOrEmpty(request.Word))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "word is required."));
        if (request.FuzzyTolerance < 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "fuzzy_tolerance must be 0 or greater."));

        try
        {
            var result = await wordGameService.ValidateWordAsync(
                request.ShareCode,
                request.Word,
                request.FuzzyTolerance,
                context.CancellationToken);

            return new ValidateWordResponse
            {
                Valid = result.Valid,
                MatchedWord = result.MatchedWord,
            };
        }
        catch (WordsetNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Wordset '{request.ShareCode}' not found."));
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }
}
