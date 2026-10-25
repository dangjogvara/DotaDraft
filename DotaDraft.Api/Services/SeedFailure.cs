namespace DotaDraft.Api.Services;

public enum SeedFailure
{
    // OpenDota could not be reached or returned unusable data
    Upstream,
    // Saving to the database failed
    Database,
    // A required table (e.g. Heroes) is empty
    MissingPrerequisite
}
