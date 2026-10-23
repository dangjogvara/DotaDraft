using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DotaDraft.Api.Data;

public static class ChangeTrackerExtensions
{
    // Entries() runs change detection, so modified properties on loaded entities are counted
    public static (int Inserted, int Updated) CountPendingWrites(this ChangeTracker changeTracker)
    {
        var entries = changeTracker.Entries().ToList();
        return (entries.Count(e => e.State == EntityState.Added), entries.Count(e => e.State == EntityState.Modified));
    }
}
