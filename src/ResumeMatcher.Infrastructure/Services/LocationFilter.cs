using System.Text.RegularExpressions;
using ResumeMatcher.Domain.Entities;

namespace ResumeMatcher.Infrastructure.Services;

/// <summary>Location filters applied to job postings.</summary>
internal static class LocationFilter
{
    // "India" itself, or a major Indian city — many postings name only the city ("Bengaluru") without the country.
    private static readonly Regex IndiaPattern = new(
        @"\bindia\b|\b(bengaluru|bangalore|mumbai|navi mumbai|new delhi|delhi|gurgaon|gurugram|noida|greater noida|hyderabad|pune|chennai|kolkata|ahmedabad|kochi|cochin|jaipur|chandigarh|indore|coimbatore|thiruvananthapuram|trivandrum|nagpur|lucknow|bhubaneswar|mysuru|mysore|vadodara|surat|visakhapatnam)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Only "india" is supported today; any other (or empty) value means "don't filter".
    // Plain "Remote" with no country is ambiguous, so it does not count as India.
    public static bool Matches(Job job, string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return true;
        if (location.Trim().Equals("india", StringComparison.OrdinalIgnoreCase))
            return IndiaPattern.IsMatch(job.Location);
        return true;
    }
}
