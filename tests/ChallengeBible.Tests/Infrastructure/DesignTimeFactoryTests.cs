using System.Reflection;
using ChallengeBible.Infrastructure.Data;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace ChallengeBible.Tests.Infrastructure;

public class DesignTimeFactoryTests
{
    [Fact]
    public void 마이그레이션_도구는_웹과_같은_user_secrets_를_읽는다()
    {
        var webId = typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId;
        Assert.Equal(DesignTimeFactory.WebUserSecretsId, webId);
    }
}
