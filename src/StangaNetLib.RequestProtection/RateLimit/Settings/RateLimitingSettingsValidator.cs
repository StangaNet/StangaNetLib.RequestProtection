using StangaNetLib.Core.Validators;

namespace StangaNetLib.RequestProtection.RateLimit.Settings;

internal sealed class RateLimitingSettingsValidator : SettingsValidatorBase<RateLimitingSettings>
{
    protected override void ValidateCore(RateLimitingSettings options, List<string> errors)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var policy in options.Policies)
        {
            if (string.IsNullOrWhiteSpace(policy.Name))
            {
                errors.Add("A rate limit policy has an empty Name.");
                continue;
            }

            if (!seenNames.Add(policy.Name))
                errors.Add($"Duplicate rate limit policy name '{policy.Name}'.");

            if (policy.Limit <= 0)
                errors.Add($"Policy '{policy.Name}': Limit must be greater than zero.");

            if (policy.WindowSeconds <= 0)
                errors.Add($"Policy '{policy.Name}': WindowSeconds must be greater than zero.");
        }

        if (options.GlobalPolicyName is { Length: > 0 } global &&
            !seenNames.Contains(global))
            errors.Add($"GlobalPolicyName '{global}' does not match any defined policy.");
    }
}
