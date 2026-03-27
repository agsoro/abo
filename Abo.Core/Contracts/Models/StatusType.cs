using System;
using System.Collections.Generic;
using System.Linq;

namespace Abo.Contracts.Models;

public static class StatusType
{
    public const string Open = "open";
    public const string Planned = "planned";
    public const string Work = "work";
    public const string Review = "review";
    public const string Check = "check";
    public const string Done = "done";
    public const string Invalid = "invalid";
    public const string WaitingCustomer = "waiting customer";

    public static readonly IReadOnlyList<string> AllowedValues = new[]
    {
        Open, Planned, Work, Review, Check, Done, Invalid, WaitingCustomer
    };

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase);
}
