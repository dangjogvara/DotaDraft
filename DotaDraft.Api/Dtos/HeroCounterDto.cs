namespace DotaDraft.Api.Dtos;

// Win rates and advantage are percentages; Advantage is the sample-adjusted
// win rate minus the counter hero's baseline, and is what counters are ranked by
public record HeroCounterDto(
    int Id,
    string LocalizedName,
    string PrimaryAttr,
    string? ImgUrl,
    decimal Advantage,
    decimal WinRate,
    decimal BaselineWinRate,
    int GamesPlayed
);
