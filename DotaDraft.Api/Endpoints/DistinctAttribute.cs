using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace DotaDraft.Api.Endpoints;

/// <summary>Requires every element of a collection to be different. A null collection is valid.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class DistinctAttribute() : ValidationAttribute("The {0} field must not contain duplicates.")
{
    public override bool IsValid(object? value)
    {
        if (value is not IEnumerable items)
            return true;

        var seen = new HashSet<object?>();
        return items.Cast<object?>().All(seen.Add);
    }
}
