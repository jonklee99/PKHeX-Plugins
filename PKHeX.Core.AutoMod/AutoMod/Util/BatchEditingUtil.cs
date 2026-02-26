using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace PKHeX.Core.AutoMod
{
    // Minimal implementation to satisfy usage in APILegality.cs
    internal static class BatchEditingUtil
    {
        public static bool IsFilterMatch(IReadOnlyList<StringInstruction> filters, object target)
        {
            if (filters == null || filters.Count == 0)
                return true;

            foreach (var f in filters)
            {
                var propName = f.PropertyName;
                var comparer = f.Comparer;
                var expected = f.PropertyValue;

                var actualObj = GetPropertyValue(target, propName);
                if (actualObj == null)
                    return false;

                var actual = actualObj.ToString() ?? string.Empty;

                // Attempt numeric comparison if both sides are numeric
                if (TryCompareNumeric(actual, expected, comparer))
                    continue;

                // Fallback to string comparisons
                switch (comparer)
                {
                    case InstructionComparer.IsEqual:
                        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    case InstructionComparer.IsNotEqual:
                        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    default:
                        // Unknown comparer: fall back to equality
                        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                }
            }

            return true;
        }

        private static object? GetPropertyValue(object target, string propertyName)
        {
            if (target == null || string.IsNullOrEmpty(propertyName))
                return null;

            // Support nested properties with dot notation
            var parts = propertyName.Split('.');
            object? current = target;
            foreach (var p in parts)
            {
                if (current == null)
                    return null;
                var t = current.GetType();
                var prop = t.GetProperty(p, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null)
                {
                    // Try field fallback
                    var field = t.GetField(p, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (field == null)
                        return null;
                    current = field.GetValue(current);
                }
                else
                {
                    current = prop.GetValue(current);
                }
            }

            return current;
        }

        private static bool TryCompareNumeric(string actualStr, string expectedStr, InstructionComparer comparer)
        {
            if (string.IsNullOrEmpty(actualStr) || string.IsNullOrEmpty(expectedStr))
                return false;

            if (!double.TryParse(actualStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var actual))
                return false;
            if (!double.TryParse(expectedStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var expected))
                return false;

            return comparer switch
            {
                InstructionComparer.IsEqual => actual == expected,
                InstructionComparer.IsNotEqual => actual != expected,
                InstructionComparer.IsGreaterThan => actual > expected,
                InstructionComparer.IsGreaterThanOrEqual => actual >= expected,
                InstructionComparer.IsLessThan => actual < expected,
                InstructionComparer.IsLessThanOrEqual => actual <= expected,
                _ => false,
            };
        }
    }
}
