using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// <see cref="Result{TValue, TError}"/> members as documented in their XML docs that the existing
/// Common/ResultTests do not cover: argument validation, None/Error propagation through
/// <c>Map</c>/<c>Bind</c>/<c>MapError</c>, the defaults and the string form.
/// </summary>
public class ResultApiTests
{
    private static readonly ComputationError Failure = new("failed");

    [Fact]
    public void Error_RejectsANullError()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int, ComputationError>.Error(null!));
    }

    [Fact]
    public void Operations_RejectNullDelegates()
    {
        var result = Result<int, ComputationError>.Success(1);

        Assert.Throws<ArgumentNullException>(() => result.Map<int>(null!));
        Assert.Throws<ArgumentNullException>(() => result.Bind<int>(null!));
        Assert.Throws<ArgumentNullException>(() => result.MapError<string>(null!));
        Assert.Throws<ArgumentNullException>(() => result.GetValueOrDefault((Func<ComputationError, int>)null!));
        Assert.Throws<ArgumentNullException>(() => result.Match<int>(null!, static _ => 0));
        Assert.Throws<ArgumentNullException>(() => result.Match(static x => x, null!, static _ => 0));
        Assert.Throws<ArgumentNullException>(() => result.Match(null!, static () => { }, static _ => { }));
    }

    [Fact]
    public void MapAndBind_PropagateErrorAndNone_WithoutInvokingTheDelegate()
    {
        var error = Result<int, ComputationError>.Error(Failure);
        var none = Result<int, ComputationError>.None;
        Func<int, int> mapper = static _ => throw new InvalidOperationException("must not run");
        Func<int, Result<int, ComputationError>> binder = static _ => throw new InvalidOperationException("must not run");

        Assert.Equal(error, error.Map(mapper));
        Assert.Equal(error, error.Bind(binder));
        Assert.True(none.Map(mapper).IsNone);
        Assert.True(none.Bind(binder).IsNone);
    }

    [Fact]
    public void Bind_ReturnsTheBindersResult()
    {
        var result = Result<int, ComputationError>.Success(2);

        Assert.Equal(Result<string, ComputationError>.Success("2"), result.Bind(static x => Result<string, ComputationError>.Success(x.ToString())));
        Assert.True(result.Bind(static _ => Result<string, ComputationError>.Error(Failure)).IsError);
        Assert.True(result.Bind(static _ => Result<string, ComputationError>.None).IsNone);
    }

    [Fact]
    public void MapError_TransformsOnlyTheError()
    {
        Assert.Equal(
            Result<int, string>.Error("failed!"),
            Result<int, ComputationError>.Error(Failure).MapError(static e => e.Message + "!"));
        Assert.Equal(
            Result<int, string>.Success(1),
            Result<int, ComputationError>.Success(1).MapError<string>(static _ => throw new InvalidOperationException()));
        Assert.True(Result<int, ComputationError>.None.MapError<string>(static _ => throw new InvalidOperationException()).IsNone);
    }

    [Fact]
    public void GetValueOrDefault_ForEachState()
    {
        Assert.Equal(1, Result<int, ComputationError>.Success(1).GetValueOrDefault(9));
        Assert.Equal(9, Result<int, ComputationError>.Error(Failure).GetValueOrDefault(9));
        Assert.Equal(9, Result<int, ComputationError>.None.GetValueOrDefault(9));

        Assert.Equal(1, Result<int, ComputationError>.Success(1).GetValueOrDefault(static _ => 9));
        Assert.Equal(6, Result<int, ComputationError>.Error(Failure).GetValueOrDefault(static e => e.Message.Length));
        Assert.Throws<InvalidOperationException>(() => Result<int, ComputationError>.None.GetValueOrDefault(static _ => 9));
    }

    [Fact]
    public void ErrorValue_IsDefaultUnlessError()
    {
        Assert.Null(Result<int, ComputationError>.Success(1).ErrorValue);
        Assert.Null(Result<int, ComputationError>.None.ErrorValue);
        Assert.Same(Failure, Result<int, ComputationError>.Error(Failure).ErrorValue);
    }

    [Fact]
    public void Equality_AndHashCode_FollowStateAndContents()
    {
        Assert.True(Result<int, ComputationError>.Success(1) == Result<int, ComputationError>.Success(1));
        Assert.True(Result<int, ComputationError>.Success(1) != Result<int, ComputationError>.Success(2));
        Assert.True(Result<int, ComputationError>.Error(new ComputationError("x")) == Result<int, ComputationError>.Error(new ComputationError("x")));
        Assert.True(Result<int, ComputationError>.None == default);
        Assert.True(Result<int, ComputationError>.Success(0) != Result<int, ComputationError>.None);
        Assert.Equal(Result<int, ComputationError>.Success(1).GetHashCode(), Result<int, ComputationError>.Success(1).GetHashCode());
        Assert.False(Result<int, ComputationError>.Success(1).Equals((object)1));
    }

    [Fact]
    public void ToString_NamesTheState()
    {
        Assert.Equal("Success(1)", Result<int, ComputationError>.Success(1).ToString());
        Assert.Equal("None", Result<int, ComputationError>.None.ToString());
        Assert.StartsWith("Error(", Result<int, ComputationError>.Error(Failure).ToString());
    }

    [Fact]
    public void ProviderResults_AreInExactlyOneState()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(1);

        var success = context.Number.GetValue();
        var none = context.Number.Where(static x => x > 5).GetValue();
        var error = context.Number.Select<int, int>(static _ => throw new InvalidOperationException()).GetValue();

        Assert.Equal([true, false, false], new[] { success.IsSuccess, success.IsNone, success.IsError });
        Assert.Equal([false, true, false], new[] { none.IsSuccess, none.IsNone, none.IsError });
        Assert.Equal([false, false, true], new[] { error.IsSuccess, error.IsNone, error.IsError });
    }
}
