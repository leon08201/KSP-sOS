using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KSPSOS
{
    internal enum CommandKind
    {
        Launch,
        Stage,
        Hold,
        Circ,
        Throttle,
        Sas,
        Abort,
        Print,
        If,
        Else,
        End,
        When,
        Set,
        Using,
        Rcs,
        GravTurn
    }

    internal enum ConditionOperator
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    internal sealed class Condition
    {
        public string Name;
        public bool IsBoolean;
        public bool BooleanValue;
        public bool Negated;
        public ConditionOperator Operator;
        public float Number;
    }

    internal sealed class ScriptCommand
    {
        public CommandKind Kind;
        public string Mode;
        public float Number;
        public string Text;
        public string Variable;
        public string Module;
        public string Body;
        public bool BoolValue;
        public int Line;
        public int JumpIndex = -1;
        public string Source;
        public Condition Condition;
    }

    internal static class CommandParser
    {
        private static readonly Regex TokenPattern = new Regex(@"[^\s,()]+", RegexOptions.Compiled);
        private static readonly HashSet<string> Modes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PG", "RG", "NML", "ANML", "RI", "RO"
        };
        private static readonly HashSet<string> Metrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ALTITUDE", "SPEED", "APOAPSIS", "PERIAPSIS", "FUEL", "OXIDIZER", "BATTERY"
        };
        private static readonly HashSet<string> Modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AUTOSTAGE", "CLEARSKIES", "AUTOWARP", "MAXSCIENCE", "SCIENCE",
            "SAFEABORT", "SMARTRCS", "AUTOLAND", "RENDEZVOUS", "DOCKING", "RECOVERY"
        };

        public static List<ScriptCommand> Parse(string source, List<string> errors)
        {
            var result = new List<ScriptCommand>();
            if (string.IsNullOrEmpty(source))
            {
                errors.Add("Script is empty.");
                return result;
            }

            string[] lines = source.Replace("\r", "").Split('\n');
            var ifStack = new Stack<int>();
            var elseStack = new Stack<int>();
            bool executableCommandSeen = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int comment = line.IndexOf('#');
                if (comment >= 0)
                    line = line.Substring(0, comment);

                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (!trimmed.EndsWith(";", StringComparison.Ordinal))
                {
                    errors.Add("Line " + (i + 1) + ": every command must end with ';'.");
                    continue;
                }

                line = trimmed.Substring(0, trimmed.Length - 1).Trim();
                List<string> tokens = Tokens(line);
                if (tokens.Count == 0)
                    continue;

                string command = tokens[0].ToUpperInvariant();
                var parsed = new ScriptCommand
                {
                    Line = i + 1,
                    Source = line
                };

                switch (command)
                {
                    case "LAUNCH":
                        parsed.Kind = CommandKind.Launch;
                        break;
                    case "STAGE":
                        parsed.Kind = CommandKind.Stage;
                        break;
                    case "HOLD":
                    case "WAIT":
                        parsed.Kind = CommandKind.Hold;
                        if (!ReadHold(tokens, parsed, errors))
                            continue;
                        break;
                    case "CIRC":
                    case "CIRCULARIZE":
                        parsed.Kind = CommandKind.Circ;
                        break;
                    case "GRAVTURN":
                        parsed.Kind = CommandKind.GravTurn;
                        if (!ReadBody(tokens, parsed, errors))
                            continue;
                        break;
                    case "THROTTLE":
                        parsed.Kind = CommandKind.Throttle;
                        if (!ReadNumber(tokens, 1, parsed, errors, "throttle"))
                            continue;
                        if (parsed.Number < 0 || parsed.Number > 100)
                        {
                            errors.Add("Line " + parsed.Line + ": throttle must be from 0 to 100.");
                            continue;
                        }
                        if (parsed.Number > 1)
                            parsed.Number /= 100f;
                        break;
                    case "SAS":
                        parsed.Kind = CommandKind.Sas;
                        if (!ReadMode(tokens, 1, parsed, errors))
                            continue;
                        break;
                    case "RCS":
                        parsed.Kind = CommandKind.Rcs;
                        if (!ReadRcs(tokens, parsed, errors))
                            continue;
                        break;
                    case "ABORT":
                        parsed.Kind = CommandKind.Abort;
                        break;
                    case "PRINT":
                        parsed.Kind = CommandKind.Print;
                        parsed.Text = RemainderAfterFirstToken(line);
                        break;
                    case "IF":
                        parsed.Kind = CommandKind.If;
                        if (!ReadCondition(tokens, 1, parsed, errors))
                            continue;
                        ifStack.Push(result.Count);
                        break;
                    case "ELSE":
                        parsed.Kind = CommandKind.Else;
                        if (ifStack.Count == 0)
                        {
                            errors.Add("Line " + parsed.Line + ": ELSE has no matching IF.");
                            continue;
                        }
                        int ifIndex = ifStack.Pop();
                        result[ifIndex].JumpIndex = result.Count + 1;
                        parsed.JumpIndex = ifIndex;
                        elseStack.Push(result.Count);
                        break;
                    case "END":
                    case "ENDIF":
                        parsed.Kind = CommandKind.End;
                        if (ifStack.Count > 0)
                        {
                            int openIf = ifStack.Pop();
                            result[openIf].JumpIndex = result.Count + 1;
                        }
                        if (elseStack.Count > 0)
                        {
                            int openElse = elseStack.Pop();
                            result[openElse].JumpIndex = result.Count;
                        }
                        break;
                    case "WHEN":
                        parsed.Kind = CommandKind.When;
                        if (!ReadCondition(tokens, 1, parsed, errors))
                            continue;
                        break;
                    case "SET":
                        parsed.Kind = CommandKind.Set;
                        if (!ReadBooleanAssignment(tokens, parsed, errors))
                            continue;
                        break;
                    case "USING":
                        parsed.Kind = CommandKind.Using;
                        if (executableCommandSeen)
                        {
                            errors.Add("Line " + parsed.Line + ": USING directives must appear before flight commands.");
                            continue;
                        }
                        if (!ReadModule(tokens, parsed, errors))
                            continue;
                        break;
                    default:
                        errors.Add("Line " + parsed.Line + ": unknown command '" + tokens[0] + "'.");
                        continue;
                }

                result.Add(parsed);
                if (parsed.Kind != CommandKind.Using)
                    executableCommandSeen = true;
            }

            while (ifStack.Count > 0)
                errors.Add("Unclosed IF starting on line " + result[ifStack.Pop()].Line + ".");
            if (elseStack.Count > 0)
                errors.Add("Unclosed ELSE block.");

            return result;
        }

        private static List<string> Tokens(string line)
        {
            var tokens = new List<string>();
            MatchCollection matches = TokenPattern.Matches(line);
            foreach (Match match in matches)
            {
                if (!string.Equals(match.Value, "THEN", StringComparison.OrdinalIgnoreCase))
                    tokens.Add(match.Value);
            }
            return tokens;
        }

        private static bool ReadHold(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count == 1)
            {
                command.Number = 1f;
                return true;
            }

            if (Modes.Contains(tokens[1]))
            {
                command.Mode = tokens[1].ToUpperInvariant();
                if (tokens.Count == 2)
                {
                    command.Number = 1f;
                    return true;
                }
                return ReadNumber(tokens, 2, command, errors, "hold duration");
            }

            return ReadNumber(tokens, 1, command, errors, "hold duration");
        }

        private static bool ReadMode(List<string> tokens, int index, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count <= index || !Modes.Contains(tokens[index]))
            {
                errors.Add("Line " + command.Line + ": expected one of PG, RG, NML, ANML, RI, or RO.");
                return false;
            }

            command.Mode = tokens[index].ToUpperInvariant();
            return true;
        }

        private static bool ReadRcs(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count != 2 || (tokens[1].ToUpperInvariant() != "ON" && tokens[1].ToUpperInvariant() != "OFF" && tokens[1].ToUpperInvariant() != "TOGGLE"))
            {
                errors.Add("Line " + command.Line + ": use RCS ON, RCS OFF, or RCS TOGGLE.");
                return false;
            }

            command.Mode = tokens[1].ToUpperInvariant();
            return true;
        }

        private static bool ReadBody(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count != 2)
            {
                errors.Add("Line " + command.Line + ": use GRAVTURN followed by a celestial body, for example GRAVTURN KERBIN.");
                return false;
            }

            command.Body = tokens[1].ToUpperInvariant();
            return true;
        }

        private static bool ReadModule(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count != 2 || !Modules.Contains(tokens[1]))
            {
                errors.Add("Line " + command.Line + ": unknown module. Available modules are AUTOSTAGE, CLEARSKIES, AUTOWARP, MAXSCIENCE, SAFEABORT, SMARTRCS, AUTOLAND, RENDEZVOUS, DOCKING, and RECOVERY.");
                return false;
            }

            command.Module = tokens[1].ToUpperInvariant();
            if (command.Module == "SCIENCE")
                command.Module = "MAXSCIENCE";
            return true;
        }

        private static bool ReadNumber(List<string> tokens, int index, ScriptCommand command, List<string> errors, string label)
        {
            if (tokens.Count <= index)
            {
                errors.Add("Line " + command.Line + ": expected a " + label + ".");
                return false;
            }

            float value;
            if (!float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.IsNaN(value))
            {
                errors.Add("Line " + command.Line + ": '" + tokens[index] + "' is not a valid " + label + ".");
                return false;
            }

            command.Number = value;
            return true;
        }

        private static bool ReadBooleanAssignment(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count < 3)
            {
                errors.Add("Line " + command.Line + ": use SET NAME = TRUE or SET NAME = FALSE.");
                return false;
            }

            string value = tokens[tokens.Count - 1].ToUpperInvariant();
            if (value != "TRUE" && value != "FALSE")
            {
                errors.Add("Line " + command.Line + ": boolean values must be TRUE or FALSE.");
                return false;
            }

            command.Variable = tokens[1].ToUpperInvariant();
            command.BoolValue = value == "TRUE";
            return true;
        }

        private static bool ReadCondition(List<string> tokens, int start, ScriptCommand command, List<string> errors)
        {
            bool negated = false;
            if (tokens.Count > start && string.Equals(tokens[start], "NOT", StringComparison.OrdinalIgnoreCase))
            {
                negated = true;
                start++;
            }

            int count = tokens.Count - start;
            if (count == 1)
            {
                string value = tokens[start].ToUpperInvariant();
                if (value == "TRUE" || value == "FALSE")
                {
                    command.Condition = new Condition
                    {
                        IsBoolean = true,
                        BooleanValue = value == "TRUE",
                        Negated = negated
                    };
                    return true;
                }

                command.Condition = new Condition
                {
                    IsBoolean = true,
                    Name = value,
                    Negated = negated
                };
                return true;
            }

            if (count != 3)
            {
                errors.Add("Line " + command.Line + ": use a flag or METRIC OPERATOR VALUE, for example ALTITUDE > 10000.");
                return false;
            }

            ConditionOperator conditionOperator;
            if (!TryReadOperator(tokens[start + 1], out conditionOperator))
            {
                errors.Add("Line " + command.Line + ": expected one of <, <=, =, !=, >=, or >.");
                return false;
            }

            float number;
            if (!float.TryParse(tokens[start + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                errors.Add("Line " + command.Line + ": condition value must be numeric.");
                return false;
            }

            if (!Metrics.Contains(tokens[start]))
            {
                errors.Add("Line " + command.Line + ": unknown telemetry name '" + tokens[start] + "'.");
                return false;
            }

            command.Condition = new Condition
            {
                Name = tokens[start].ToUpperInvariant(),
                Operator = conditionOperator,
                Number = number,
                Negated = negated
            };
            return true;
        }

        private static bool TryReadOperator(string token, out ConditionOperator conditionOperator)
        {
            switch (token)
            {
                case "=":
                case "==": conditionOperator = ConditionOperator.Equal; return true;
                case "!=": conditionOperator = ConditionOperator.NotEqual; return true;
                case "<": conditionOperator = ConditionOperator.Less; return true;
                case "<=": conditionOperator = ConditionOperator.LessOrEqual; return true;
                case ">": conditionOperator = ConditionOperator.Greater; return true;
                case ">=": conditionOperator = ConditionOperator.GreaterOrEqual; return true;
                default: conditionOperator = ConditionOperator.Equal; return false;
            }
        }

        private static string RemainderAfterFirstToken(string line)
        {
            Match first = TokenPattern.Match(line);
            if (!first.Success || first.Index + first.Length >= line.Length)
                return string.Empty;
            return line.Substring(first.Index + first.Length).Trim();
        }
    }
}
