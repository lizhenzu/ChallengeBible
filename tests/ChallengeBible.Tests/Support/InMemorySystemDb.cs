using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Support;

/// <summary>시스템 연결 대역: 같은 메모리 DB 를 전역 필터 없이 본다.</summary>
public sealed class InMemorySystemDb(string name) : ISystemDb
{
    public AppDbContext Create(int actorAccountId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options, tenantId: null) { FilterTenants = false };
}
