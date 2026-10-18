namespace DotaDraft.Api.Dtos;

public record HeroDto(
    int Id,
    string Name,
    string LocalizedName,
    string PrimaryAttr,
    string[] Roles,
    string? ImgUrl
);
