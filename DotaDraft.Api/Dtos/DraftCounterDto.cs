namespace DotaDraft.Api.Dtos;

// A counter-pick against a whole enemy lineup. TotalAdvantage (percentage points) is the sum of the
// per-enemy advantages and is what suggestions are ranked by
public record DraftCounterDto(
    int Id,
    string LocalizedName,
    string PrimaryAttr,
    string? ImgUrl,
    decimal TotalAdvantage,
    decimal BaselineWinRate,
    IReadOnlyList<EnemyMatchupDto> Matchups
);

// One entry per enemy, in the order requested. Without matchup data, Advantage is 0 and WinRate is null
public record EnemyMatchupDto(
    int EnemyId,
    decimal Advantage,
    decimal? WinRate,
    int GamesPlayed
);
