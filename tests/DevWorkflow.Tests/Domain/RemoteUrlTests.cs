using DevWorkflow.Domain;
using DevWorkflow.Domain.Repositories;

namespace DevWorkflow.Tests.Domain;

public class RemoteUrlTests
{
    [Theory]
    [InlineData("git@github.com:example-org/sample-api.git")]
    [InlineData("git@github.com:example-org/sample-api")]
    [InlineData("https://github.com/example-org/sample-api.git")]
    [InlineData("https://github.com/Example-Org/Sample-Api")]
    [InlineData("https://user@github.com/example-org/sample-api.git/")]
    [InlineData("ssh://git@github.com/example-org/sample-api.git")]
    [InlineData("ssh://git@github.com:22/example-org/sample-api.git")]
    [InlineData("github.com/example-org/sample-api")]
    public void Ssh_and_https_forms_of_the_same_remote_normalize_equal(string remote)
    {
        Assert.Equal("github.com/example-org/sample-api", RemoteUrl.Normalize(remote));
    }

    [Fact]
    public void Bitbucket_remotes_keep_their_host()
    {
        Assert.Equal(
            "bitbucket.org/example-team/sample-web",
            RemoteUrl.Normalize("git@bitbucket.org:example-team/sample-web.git"));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://github.com/")]
    [InlineData("https://github.com/only-owner")]
    public void Unrecognizable_remotes_are_rejected(string remote)
    {
        var error = Assert.Throws<DomainException>(() => RemoteUrl.Normalize(remote));
        Assert.Equal("invalid_remote_url", error.Code);
    }
}
