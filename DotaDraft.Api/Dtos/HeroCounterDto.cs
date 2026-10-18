namespace DotaDraft.Api.Dtos;

public record HeroCounterDto(
    int Id,
    string LocalizedName,
    string PrimaryAttr,
    string? ImgUrl,
    decimal WinRate,
    int GamesPlayed
);
