using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Sentinel.Core.Models;

namespace Sentinel.Core.Rules
{
    /// <summary>Flat, case-insensitive property bag describing a door for rule evaluation.</summary>
    public class DoorFacts
    {
        public const string Mark = "Mark";
        public const string FamilyName = "Family";
        public const string TypeName = "Type";
        public const string Level = "Level";
        public const string SideARoom = "SideARoom";
        public const string SideBRoom = "SideBRoom";
        public const string AnyRoom = "AnyRoom";
        public const string WidthMm = "WidthMm";
        public const string HeightMm = "HeightMm";
        public const string IfcGlobalId = "IfcGlobalId";
        public const string LinkName = "Link";

        public static readonly string[] BuiltInFields =
        {
            Mark, FamilyName, TypeName, Level, SideARoom, SideBRoom, AnyRoom, WidthMm, HeightMm, IfcGlobalId, LinkName
        };

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string this[string field]
        {
            get
            {
                string v;
                return field != null && _values.TryGetValue(field, out v) ? v : null;
            }
            set
            {
                if (field != null) _values[field] = value;
            }
        }

        public IEnumerable<string> Fields => _values.Keys;

        public static DoorFacts FromSource(SourceDoorReference src)
        {
            var f = new DoorFacts();
            if (src == null) return f;

            if (src.LastKnownParameters != null)
                foreach (var kv in src.LastKnownParameters) f[kv.Key] = kv.Value;

            f[Mark] = src.Mark;
            f[FamilyName] = src.FamilyName;
            f[TypeName] = src.TypeName;
            f[Level] = src.LevelName;
            f[SideARoom] = src.SideARoom;
            f[SideBRoom] = src.SideBRoom;
            f[AnyRoom] = string.Join(" | ", new[] { src.SideARoom, src.SideBRoom }.Where(s => !string.IsNullOrWhiteSpace(s)));
            f[IfcGlobalId] = src.IfcGlobalId;
            f[LinkName] = src.LinkName;
            if (src.LastKnownGeometry != null)
            {
                f[WidthMm] = src.LastKnownGeometry.WidthMm.ToString("0", CultureInfo.InvariantCulture);
                f[HeightMm] = src.LastKnownGeometry.HeightMm.ToString("0", CultureInfo.InvariantCulture);
            }
            return f;
        }
    }

    public class RuleSuggestion
    {
        public AssignmentRule Rule { get; set; }
        public string DefinitionId { get; set; }
        public string Explanation { get; set; }
    }

    public static class AssignmentRuleEvaluator
    {
        /// <summary>Returns the first enabled rule (by priority, then list order) whose conditions match, or null.</summary>
        public static RuleSuggestion Suggest(IEnumerable<AssignmentRule> rules, DoorFacts facts)
        {
            if (rules == null || facts == null) return null;

            var ordered = rules
                .Select((r, i) => new { Rule = r, Index = i })
                .Where(x => x.Rule != null && x.Rule.Enabled && !string.IsNullOrEmpty(x.Rule.DefinitionId))
                .OrderBy(x => x.Rule.Priority)
                .ThenBy(x => x.Index);

            foreach (var x in ordered)
            {
                string explanation;
                if (Matches(x.Rule, facts, out explanation))
                {
                    return new RuleSuggestion
                    {
                        Rule = x.Rule,
                        DefinitionId = x.Rule.DefinitionId,
                        Explanation = "Rule \"" + (x.Rule.Name ?? "(unnamed)") + "\": " + explanation
                    };
                }
            }
            return null;
        }

        public static bool Matches(AssignmentRule rule, DoorFacts facts, out string explanation)
        {
            explanation = "";
            var conditions = rule?.Conditions ?? new List<RuleCondition>();
            if (conditions.Count == 0) return false; // an empty rule never matches (avoids accidental catch-all)

            var parts = new List<string>();
            if (rule.MatchMode == RuleMatchMode.Any)
            {
                foreach (var c in conditions)
                {
                    if (Evaluate(c, facts))
                    {
                        explanation = Describe(c, facts);
                        return true;
                    }
                }
                return false;
            }

            foreach (var c in conditions)
            {
                if (!Evaluate(c, facts)) return false;
                parts.Add(Describe(c, facts));
            }
            explanation = string.Join(" AND ", parts);
            return true;
        }

        public static bool Evaluate(RuleCondition c, DoorFacts facts)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Field)) return false;

            var actual = (facts[c.Field] ?? "").Trim();
            var expected = (c.Value ?? "").Trim();

            switch (c.Operator)
            {
                case RuleOperator.Equals:
                    return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) || NumbersEqual(actual, expected);
                case RuleOperator.NotEquals:
                    return !(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) || NumbersEqual(actual, expected));
                case RuleOperator.Contains:
                    return expected.Length > 0 && actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
                case RuleOperator.NotContains:
                    return expected.Length == 0 || actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0;
                case RuleOperator.StartsWith:
                    return expected.Length > 0 && actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
                case RuleOperator.GreaterThan:
                    {
                        double a, e;
                        return TryParseNumber(actual, out a) && TryParseNumber(expected, out e) && a > e;
                    }
                case RuleOperator.LessThan:
                    {
                        double a, e;
                        return TryParseNumber(actual, out a) && TryParseNumber(expected, out e) && a < e;
                    }
                case RuleOperator.IsEmpty:
                    return actual.Length == 0;
                case RuleOperator.IsNotEmpty:
                    return actual.Length > 0;
                default:
                    return false;
            }
        }

        private static bool NumbersEqual(string a, string b)
        {
            double x, y;
            return TryParseNumber(a, out x) && TryParseNumber(b, out y) && Math.Abs(x - y) < 1e-6;
        }

        private static readonly Regex NumberRegex = new Regex(@"^\s*(-?\d+(?:[.,]\d+)?)", RegexOptions.Compiled);

        /// <summary>Parses the leading number of a value ("1000", "1000 mm", "90,5"). Invariant culture, comma tolerated.</summary>
        public static bool TryParseNumber(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var m = NumberRegex.Match(s);
            if (!m.Success) return false;
            return double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static string OperatorText(RuleOperator op)
        {
            switch (op)
            {
                case RuleOperator.Equals: return "equals";
                case RuleOperator.NotEquals: return "does not equal";
                case RuleOperator.Contains: return "contains";
                case RuleOperator.NotContains: return "does not contain";
                case RuleOperator.StartsWith: return "starts with";
                case RuleOperator.GreaterThan: return "greater than";
                case RuleOperator.LessThan: return "less than";
                case RuleOperator.IsEmpty: return "is empty";
                case RuleOperator.IsNotEmpty: return "is not empty";
                default: return op.ToString();
            }
        }

        /// <summary>
        /// One line per condition for the rule test: the door's actual value, the check and whether it passed, e.g.
        /// <c>✓ AR_Uks.001_Nimetus "Teras siseuks kahepoolne" contains "kahepoolne"</c>. A field the door has no value for
        /// says so, because that usually means the parameter is not captured (or the doors were read before it was).
        /// </summary>
        public static List<string> ExplainConditions(AssignmentRule rule, DoorFacts facts)
        {
            var lines = new List<string>();
            foreach (var c in rule?.Conditions ?? new List<RuleCondition>())
            {
                if (c == null || string.IsNullOrWhiteSpace(c.Field))
                {
                    lines.Add("✗ (condition without a field)");
                    continue;
                }
                var ok = Evaluate(c, facts ?? new DoorFacts());
                var actual = facts?[c.Field];
                var value = string.IsNullOrEmpty(actual) ? "(no value on this door)" : "\"" + actual + "\"";
                var check = c.Operator == RuleOperator.IsEmpty || c.Operator == RuleOperator.IsNotEmpty
                    ? OperatorText(c.Operator)
                    : OperatorText(c.Operator) + " \"" + (c.Value ?? "") + "\"";
                lines.Add((ok ? "✓ " : "✗ ") + c.Field + " " + value + " " + check);
            }
            return lines;
        }

        private static string Describe(RuleCondition c, DoorFacts facts)
        {
            var actual = facts[c.Field] ?? "";
            if (c.Operator == RuleOperator.IsEmpty || c.Operator == RuleOperator.IsNotEmpty)
                return c.Field + " " + OperatorText(c.Operator);
            return c.Field + " " + OperatorText(c.Operator) + " \"" + c.Value + "\" (is \"" + actual + "\")";
        }
    }
}
