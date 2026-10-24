namespace DotaDraft.Api.Dtos;

/// <param name="Id">OpenDota's hero ID.</param>
/// <param name="Name">Internal name, e.g. npc_dota_hero_antimage.</param>
/// <param name="LocalizedName">Display name, e.g. Anti-Mage.</param>
/// <param name="PrimaryAttr">str, agi, int or all.</param>
/// <param name="Roles">e.g. Carry, Escape.</param>
/// <param name="ImgUrl">Portrait on Steam's CDN, if OpenDota has one.</param>
public record HeroDto(
    int Id,
    string Name,
    string LocalizedName,
    string PrimaryAttr,
    string[] Roles,
    string? ImgUrl
);
