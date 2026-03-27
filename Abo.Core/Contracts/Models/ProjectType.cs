using System;
using System.Collections.Generic;
using System.Linq;

namespace Abo.Contracts.Models;

public static class ProjectType
{
    public const string Requested = "requested";
    public const string ReleaseCurrent = "release-current";
    public const string ReleaseNext = "release-next";
    public const string Backlog = "backlog";

    public static readonly IReadOnlyList<string> AllowedValues = new[]
    {
        Requested, ReleaseCurrent, ReleaseNext, Backlog
    };

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase);
}
