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
        GravTurn,
        KeepSpeed,
        PolarOrbit,
        Hohmann,
        Transfer,
        Science,
        DeploySolarPanels,
        Distance,
        LaunchToDock,
        Rendezvous,
        DockToTarget,
        AutoDock,
        ApolloDock,
        Land,
        Recover,
        WalkInThePark,
        ExecuteNode,
        AlignPlane
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
        public bool IsOrbitState;
        public bool OrbitSafe;
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
        public bool Polar;
        public string TargetName;
        public bool BoolValue;
        public bool HasTargetAltitude;
        public int Line;
        public int JumpIndex = -1;
        public string Source;
        public Condition Condition;
        public bool OpensBlock;
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
            "SAFEABORT", "SMARTRCS", "AUTOLAND", "RENDEZVOUS", "DOCKING", "RECOVERY",
            "CTRLOCK", "MANUALSTAGE"
        };

        public static List<ScriptCommand> Parse(string source, List<string> errors)
        {
            var result = new List<ScriptCommand>();
            if (string.IsNullOrEmpty(source))
            {
                errors.Add("Script is empty.");
                return result;
            }

            source = ExpandGravityTurnBlocks(source.Replace("\r", ""));
            source = ExpandInlineBraces(source);
            string[] lines = source.Split('\n');
            var ifStack = new Stack<int>();
            var elseStack = new Stack<int>();
            var blockStack = new Stack<int>();
            int lastClosedIf = -1;
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

                bool closesBlock = trimmed == "}" || trimmed == "};";
                bool opensBlock = trimmed.EndsWith("{", StringComparison.Ordinal);
                bool hasSemicolon = trimmed.EndsWith(";", StringComparison.Ordinal);
                if (!closesBlock && !opensBlock && blockStack.Count == 0 && !hasSemicolon)
                {
                    errors.Add("Line " + (i + 1) + ": every command must end with ';'.");
                    continue;
                }

                if (closesBlock)
                {
                    if (blockStack.Count == 0)
                    {
                        errors.Add("Line " + (i + 1) + ": unexpected '}'.");
                        continue;
                    }

                    int openIndex = blockStack.Pop();
                    ScriptCommand openCommand = result[openIndex];
                    if (openCommand.Kind == CommandKind.If)
                    {
                        if (ifStack.Count > 0 && ifStack.Peek() == openIndex)
                            ifStack.Pop();
                        openCommand.JumpIndex = result.Count + 1;
                        lastClosedIf = openIndex;
                    }
                    else if (openCommand.Kind == CommandKind.Else)
                    {
                        if (elseStack.Count > 0 && elseStack.Peek() == openIndex)
                            elseStack.Pop();
                        openCommand.JumpIndex = result.Count;
                    }

                    result.Add(new ScriptCommand
                    {
                        Kind = CommandKind.End,
                        Line = i + 1,
                        Source = "}"
                    });
                    continue;
                }

                line = (opensBlock || hasSemicolon)
                    ? trimmed.Substring(0, trimmed.Length - 1).Trim()
                    : trimmed;
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
                        if (tokens.Count > 1 && !ReadLaunch(tokens, parsed, errors))
                            continue;
                        break;
                    case "STAGE":
                        parsed.Kind = CommandKind.Stage;
                        if (!RequireTokenCount(tokens, 1, parsed, errors, "use STAGE;"))
                            continue;
                        break;
                    case "HOLD":
                    case "WAIT":
                        parsed.Kind = CommandKind.Hold;
                        if (!ReadHold(tokens, parsed, errors))
                            continue;
                        break;
                    case "CIRC":
                    case "CIRCULARIZE":
                    case "CIRCULARISE":
                        parsed.Kind = CommandKind.Circ;
                        if (!ReadCircularize(tokens, parsed, errors))
                            continue;
                        break;
                    case "GRAVTURN":
                        parsed.Kind = CommandKind.GravTurn;
                        if (!ReadGravityTurn(tokens, parsed, errors))
                            continue;
                        break;
                    case "KEEP":
                        parsed.Kind = CommandKind.KeepSpeed;
                        if (!ReadKeepSpeed(tokens, parsed, errors))
                            continue;
                        break;
                    case "POLAR":
                        parsed.Kind = CommandKind.PolarOrbit;
                        if (!ReadPolarOrbit(tokens, parsed, errors))
                            continue;
                        break;
                    case "HOHMANN":
                        parsed.Kind = CommandKind.Hohmann;
                        if (!ReadTransfer(tokens, parsed, errors))
                            continue;
                        break;
                    case "TRANSFER":
                        parsed.Kind = CommandKind.Transfer;
                        if (!ReadTransfer(tokens, parsed, errors))
                            continue;
                        break;
                    case "EXEC":
                    case "EXECUTE":
                        parsed.Kind = CommandKind.ExecuteNode;
                        if (!ReadExecute(tokens, parsed, errors))
                            continue;
                        break;
                    case "ALIGN":
                        parsed.Kind = CommandKind.AlignPlane;
                        if (!ReadAlignPlane(tokens, parsed, errors))
                            continue;
                        break;
                    case "SCIENCE":
                    case "DO":
                        parsed.Kind = CommandKind.Science;
                        if (!ReadScience(tokens, parsed, errors))
                            continue;
                        break;
                    case "DEPLOY":
                        parsed.Kind = CommandKind.DeploySolarPanels;
                        if (!ReadDeploy(tokens, parsed, errors))
                            continue;
                        break;
                    case "DISTANCE":
                        parsed.Kind = CommandKind.Distance;
                        if (!ReadDistance(tokens, 1, parsed, errors, "distance"))
                            continue;
                        break;
                    case "*WALK":
                        parsed.Kind = CommandKind.WalkInThePark;
                        if (!IsWalkInThePark(tokens))
                        {
                            errors.Add("Line " + parsed.Line + ": use *WALK IN THE PARK*;.");
                            continue;
                        }
                        break;
                    case "RENDEZVOUS":
                        parsed.Kind = CommandKind.Rendezvous;
                        if (!ReadRendezvous(tokens, parsed, errors))
                            continue;
                        break;
                    case "DOCK":
                        parsed.Kind = CommandKind.DockToTarget;
                        if (!ReadDock(tokens, parsed, errors))
                            continue;
                        break;
                    case "AUTODOCK":
                        parsed.Kind = CommandKind.AutoDock;
                        if (tokens.Count != 1)
                        {
                            errors.Add("Line " + parsed.Line + ": use AUTODOCK;.");
                            continue;
                        }
                        break;
                    case "APOLLO":
                    case "TRANSPOSE":
                        parsed.Kind = CommandKind.ApolloDock;
                        if (!ReadApolloDock(tokens, parsed, errors))
                            continue;
                        break;
                    case "LAND":
                        parsed.Kind = CommandKind.Land;
                        if (!ReadLand(tokens, parsed, errors))
                            continue;
                        break;
                    case "RECOVER":
                        parsed.Kind = CommandKind.Recover;
                        if (tokens.Count != 1)
                        {
                            errors.Add("Line " + parsed.Line + ": use RECOVER;.");
                            continue;
                        }
                        break;
                    case "THROTTLE":
                        parsed.Kind = CommandKind.Throttle;
                        if (!RequireTokenCount(tokens, 2, parsed, errors, "use THROTTLE VALUE;"))
                            continue;
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
                        if (!RequireTokenCount(tokens, 2, parsed, errors, "use SAS PG;"))
                            continue;
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
                        if (!RequireTokenCount(tokens, 1, parsed, errors, "use ABORT;"))
                            continue;
                        break;
                    case "PRINT":
                        parsed.Kind = CommandKind.Print;
                        parsed.Text = RemainderAfterFirstToken(line);
                        break;
                    case "IF":
                        parsed.Kind = CommandKind.If;
                        parsed.OpensBlock = opensBlock;
                        if (!ReadCondition(tokens, 1, parsed, errors))
                            continue;
                        ifStack.Push(result.Count);
                        break;
                    case "ELSE":
                        parsed.Kind = CommandKind.Else;
                        parsed.OpensBlock = opensBlock;
                        if (!RequireTokenCount(tokens, 1, parsed, errors, "use ELSE; or ELSE {"))
                            continue;
                        int ifIndex;
                        if (ifStack.Count > 0)
                            ifIndex = ifStack.Pop();
                        else if (lastClosedIf >= 0)
                        {
                            ifIndex = lastClosedIf;
                            lastClosedIf = -1;
                        }
                        else
                        {
                            errors.Add("Line " + parsed.Line + ": ELSE has no matching IF.");
                            continue;
                        }
                        result[ifIndex].JumpIndex = result.Count + 1;
                        parsed.JumpIndex = ifIndex;
                        elseStack.Push(result.Count);
                        break;
                    case "END":
                    case "ENDIF":
                        parsed.Kind = CommandKind.End;
                        if (!RequireTokenCount(tokens, 1, parsed, errors, "use END; or ENDIF;"))
                            continue;
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
                        parsed.OpensBlock = opensBlock;
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
                if (parsed.OpensBlock)
                {
                    if (parsed.Kind != CommandKind.If && parsed.Kind != CommandKind.Else && parsed.Kind != CommandKind.When)
                    {
                        errors.Add("Line " + parsed.Line + ": only IF, ELSE, and WHEN may open a '{' block.");
                        result.RemoveAt(result.Count - 1);
                        continue;
                    }
                    blockStack.Push(result.Count - 1);
                }
                if (parsed.Kind != CommandKind.Using)
                    executableCommandSeen = true;
            }

            while (ifStack.Count > 0)
                errors.Add("Unclosed IF starting on line " + result[ifStack.Pop()].Line + ".");
            if (elseStack.Count > 0)
                errors.Add("Unclosed ELSE block.");
            while (blockStack.Count > 0)
                errors.Add("Unclosed '{' block starting on line " + result[blockStack.Pop()].Line + ".");

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

        private static string ExpandInlineBraces(string source)
        {
            var expandedLines = new List<string>();
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string original = lines[i];
                int commentIndex = original.IndexOf('#');
                string code = commentIndex >= 0 ? original.Substring(0, commentIndex) : original;
                string comment = commentIndex >= 0 ? original.Substring(commentIndex) : string.Empty;
                int cursor = 0;

                while (cursor < code.Length)
                {
                    int braceIndex = code.IndexOfAny(new[] { '{', '}' }, cursor);
                    if (braceIndex < 0)
                        break;

                    string before = code.Substring(cursor, braceIndex - cursor).Trim();
                    if (code[braceIndex] == '{')
                    {
                        expandedLines.Add(before.Length == 0 ? "{" : before + " {");
                        cursor = braceIndex + 1;
                        continue;
                    }

                    if (before.Length > 0)
                        expandedLines.Add(before);

                    int afterBrace = braceIndex + 1;
                    while (afterBrace < code.Length && char.IsWhiteSpace(code[afterBrace]))
                        afterBrace++;
                    bool hasSemicolon = afterBrace < code.Length && code[afterBrace] == ';';
                    if (hasSemicolon)
                        afterBrace++;
                    expandedLines.Add(hasSemicolon ? "};" : "}");
                    cursor = afterBrace;
                }

                string remainder = code.Substring(cursor).Trim();
                if (remainder.Length > 0)
                    expandedLines.Add(remainder + comment);
                else if (comment.Length > 0)
                    expandedLines.Add(comment);
            }
            return string.Join("\n", expandedLines.ToArray());
        }

        private static string ExpandGravityTurnBlocks(string source)
        {
            return Regex.Replace(source,
                @"(?im)^\s*GRAVTURN(?:\s+(POLAR|EQUATORIAL))?(?:\s+([A-Za-z0-9_*]+))?\s*\{\s*AUTOCIRC(?:\s+([^\s}]+))?\s*\}\s*;?\s*(#.*)?$",
                match =>
                {
                    string mode = match.Groups[1].Success ? match.Groups[1].Value + " " : string.Empty;
                    string body = match.Groups[2].Success ? match.Groups[2].Value + " " : string.Empty;
                    string altitude = match.Groups[3].Success ? match.Groups[3].Value : "80KM";
                    string comment = match.Groups[4].Success ? " " + match.Groups[4].Value : string.Empty;
                    return "GRAVTURN " + mode + body + "AUTOCIRC " + altitude + ";" + comment;
                });
        }

        private static bool ReadLaunch(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count < 5 || !tokens[1].Equals("TO", StringComparison.OrdinalIgnoreCase) ||
                !tokens[2].Equals("DOCK", StringComparison.OrdinalIgnoreCase) ||
                !tokens[3].Equals("TO", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Line " + command.Line + ": use LAUNCH; or LAUNCH TO DOCK TO VESSEL NAME;.");
                return false;
            }

            command.Kind = CommandKind.LaunchToDock;
            command.TargetName = JoinTokens(tokens, 4);
            return command.TargetName.Length > 0;
        }

        private static bool ReadCircularize(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count == 1)
                return true;
            if (tokens.Count != 2 || !TryParseDistance(tokens[1], out command.Number))
            {
                errors.Add("Line " + command.Line + ": use CIRC; or CIRCULARISE 100KM;.");
                return false;
            }

            command.HasTargetAltitude = true;
            return command.Number > 0f;
        }

        private static bool ReadDistance(List<string> tokens, int index, ScriptCommand command, List<string> errors, string label)
        {
            if (tokens.Count != index + 1 || !TryParseDistance(tokens[index], out command.Number) || command.Number < 0f)
            {
                errors.Add("Line " + command.Line + ": use DISTANCE followed by metres, for example DISTANCE 50;.");
                return false;
            }
            return true;
        }

        private static bool TryParseDistance(string token, out float metres)
        {
            metres = 0f;
            if (string.IsNullOrEmpty(token))
                return false;

            string value = token.ToUpperInvariant();
            float multiplier = 1f;
            if (value.EndsWith("KM", StringComparison.Ordinal))
            {
                multiplier = 1000f;
                value = value.Substring(0, value.Length - 2);
            }
            else if (value.EndsWith("M", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
            }

            float parsed;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || float.IsNaN(parsed))
                return false;
            metres = parsed * multiplier;
            return true;
        }

        private static bool ReadRendezvous(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count < 3 || !tokens[1].Equals("WITH", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Line " + command.Line + ": use RENDEZVOUS WITH TARGET; or RENDEZVOUS WITH VESSEL NAME;.");
                return false;
            }
            command.TargetName = JoinTokens(tokens, 2);
            return true;
        }

        private static bool ReadDock(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count != 3 || !tokens[1].Equals("TO", StringComparison.OrdinalIgnoreCase) ||
                !tokens[2].Equals("TARGET", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Line " + command.Line + ": use DOCK TO TARGET;.");
                return false;
            }
            return true;
        }

        private static bool ReadApolloDock(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            bool valid = (tokens.Count == 2 && tokens[0].Equals("APOLLO", StringComparison.OrdinalIgnoreCase) &&
                          tokens[1].Equals("DOCK", StringComparison.OrdinalIgnoreCase)) ||
                         (tokens.Count == 2 && tokens[0].Equals("TRANSPOSE", StringComparison.OrdinalIgnoreCase) &&
                          tokens[1].Equals("DOCK", StringComparison.OrdinalIgnoreCase));
            bool targeted = tokens.Count == 4 && tokens[0].Equals("APOLLO", StringComparison.OrdinalIgnoreCase) &&
                            tokens[1].Equals("DOCK", StringComparison.OrdinalIgnoreCase) &&
                            tokens[2].Equals("TO", StringComparison.OrdinalIgnoreCase) &&
                            tokens[3].Equals("TARGET", StringComparison.OrdinalIgnoreCase);
            if (!valid)
            {
                if (!targeted)
                {
                    errors.Add("Line " + command.Line + ": use APOLLO DOCK;, APOLLO DOCK TO TARGET;, or TRANSPOSE DOCK;.");
                    return false;
                }
            }
            if (targeted)
                command.TargetName = "TARGET";
            if (!valid && !targeted)
                return false;
            return true;
        }

        private static bool ReadLand(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count < 3 || !tokens[1].Equals("ON", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Line " + command.Line + ": use LAND ON BODY;.");
                return false;
            }
            int start = tokens[2].Equals("THE", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            if (tokens.Count <= start)
            {
                errors.Add("Line " + command.Line + ": LAND needs a celestial body.");
                return false;
            }
            command.Body = JoinTokens(tokens, start).ToUpperInvariant();
            return true;
        }

        private static bool IsWalkInThePark(List<string> tokens)
        {
            return tokens.Count == 4 && tokens[0].Equals("*WALK", StringComparison.OrdinalIgnoreCase) &&
                   tokens[1].Equals("IN", StringComparison.OrdinalIgnoreCase) &&
                   tokens[2].Equals("THE", StringComparison.OrdinalIgnoreCase) &&
                   tokens[3].Equals("PARK*", StringComparison.OrdinalIgnoreCase);
        }

        private static string JoinTokens(List<string> tokens, int start)
        {
            string joined = string.Empty;
            for (int i = start; i < tokens.Count; i++)
            {
                if (joined.Length > 0)
                    joined += " ";
                joined += tokens[i];
            }
            return joined.Trim('*');
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
                if (tokens.Count != 3)
                {
                    errors.Add("Line " + command.Line + ": use HOLD MODE SECONDS;.");
                    return false;
                }
                return ReadNumber(tokens, 2, command, errors, "hold duration");
            }

            if (tokens.Count != 2)
            {
                errors.Add("Line " + command.Line + ": use HOLD SECONDS; or HOLD MODE SECONDS;.");
                return false;
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

        private static bool ReadGravityTurn(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            int index = 1;
            command.Polar = false;
            if (tokens.Count > index &&
                (tokens[index].Equals("POLAR", StringComparison.OrdinalIgnoreCase) ||
                 tokens[index].Equals("EQUATORIAL", StringComparison.OrdinalIgnoreCase)))
            {
                command.Polar = tokens[index].Equals("POLAR", StringComparison.OrdinalIgnoreCase);
                index++;
            }

            if (tokens.Count <= index)
            {
                errors.Add("Line " + command.Line + ": use GRAVTURN BODY or GRAVTURN BODY { AUTOCIRC 80KM }.");
                return false;
            }

            if (tokens[index].Equals("AUTOCIRC", StringComparison.OrdinalIgnoreCase))
            {
                command.Body = string.Empty;
            }
            else
            {
                command.Body = tokens[index].ToUpperInvariant();
                index++;
            }

            if (tokens.Count == index)
                return true;

            if (!tokens[index].Equals("AUTOCIRC", StringComparison.OrdinalIgnoreCase) || tokens.Count > index + 2)
            {
                errors.Add("Line " + command.Line + ": use GRAVTURN BODY { AUTOCIRC 80KM }.");
                return false;
            }

            command.HasTargetAltitude = true;
            command.Number = 80000f;
            if (tokens.Count == index + 2 && !TryParseDistance(tokens[index + 1], out command.Number))
            {
                errors.Add("Line " + command.Line + ": AUTOCIRC altitude must be a distance such as 80KM.");
                return false;
            }
            return true;
        }

        private static bool ReadKeepSpeed(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count < 3 || !tokens[1].Equals("SPEED", StringComparison.OrdinalIgnoreCase) || tokens.Count > 4)
            {
                errors.Add("Line " + command.Line + ": use KEEP SPEED VALUE; or KEEP SPEED SURFACE VALUE;.");
                return false;
            }

            int valueIndex = 2;
            command.Mode = "SURFACE";
            if (tokens.Count == 4)
            {
                string mode = tokens[2].ToUpperInvariant();
                if (mode != "SURFACE" && mode != "ORBITAL" && mode != "VERTICAL")
                {
                    errors.Add("Line " + command.Line + ": KEEP SPEED mode must be SURFACE, ORBITAL, or VERTICAL.");
                    return false;
                }
                command.Mode = mode;
                valueIndex = 3;
            }

            string value = tokens[valueIndex].ToUpperInvariant();
            if (value.EndsWith("M/S", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 3);
            else if (value.EndsWith("MS", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 2);
            float speed;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out speed) ||
                float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f)
            {
                errors.Add("Line " + command.Line + ": speed must be a non-negative value such as 250M/S.");
                return false;
            }
            command.Number = speed;
            return true;
        }

        private static bool ReadPolarOrbit(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count == 2 && tokens[1].Equals("ORBIT", StringComparison.OrdinalIgnoreCase))
            {
                command.Body = string.Empty;
                return true;
            }

            if (tokens.Count >= 4 && tokens[1].Equals("ORBIT", StringComparison.OrdinalIgnoreCase) &&
                tokens[2].Equals("AROUND", StringComparison.OrdinalIgnoreCase))
            {
                command.Body = JoinTokens(tokens, 3).ToUpperInvariant();
                return command.Body.Length > 0;
            }

            errors.Add("Line " + command.Line + ": use POLAR ORBIT; or POLAR ORBIT AROUND BODY;.");
            return false;
        }

        private static bool ReadTransfer(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            bool validShortForm = tokens.Count == 3 &&
                                  tokens[1].ToUpperInvariant() == "TO";
            bool validCelestialBodyForm = tokens.Count == 4 &&
                                          tokens[1].ToUpperInvariant() == "TO" &&
                                          tokens[2].ToUpperInvariant() == "CB";
            if (!validShortForm && !validCelestialBodyForm)
            {
                errors.Add("Line " + command.Line + ": use HOHMANN TO BODY or TRANSFER TO CB BODY.");
                return false;
            }

            command.Body = tokens[tokens.Count - 1].ToUpperInvariant();
            return true;
        }

        private static bool ReadExecute(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count == 1)
                return true;
            if (tokens.Count != 2 || !tokens[1].Equals("NODE", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Line " + command.Line + ": use EXEC; or EXECUTE NODE;.");
                return false;
            }
            return true;
        }

        private static bool ReadAlignPlane(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count != 4 || !tokens[1].Equals("PLANE", StringComparison.OrdinalIgnoreCase) ||
                !tokens[2].Equals("TO", StringComparison.OrdinalIgnoreCase) ||
                !tokens[3].Equals("TARGET", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Line " + command.Line + ": use ALIGN PLANE TO TARGET;.");
                return false;
            }
            return true;
        }

        private static bool ReadScience(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            int modeIndex = tokens[0].Equals("DO", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            if (tokens[0].Equals("DO", StringComparison.OrdinalIgnoreCase) && (tokens.Count < 2 || !tokens[1].Equals("SCIENCE", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("Line " + command.Line + ": use DO SCIENCE or SCIENCE AUTO.");
                return false;
            }

            if (tokens.Count <= modeIndex)
            {
                command.Mode = "AUTO";
                return true;
            }

            if (tokens.Count != modeIndex + 1)
            {
                errors.Add("Line " + command.Line + ": use SCIENCE AUTO, SCIENCE COLLECT, or SCIENCE TRANSMIT.");
                return false;
            }

            string mode = tokens[modeIndex].ToUpperInvariant();
            if (mode != "AUTO" && mode != "COLLECT" && mode != "TRANSMIT")
            {
                errors.Add("Line " + command.Line + ": use SCIENCE AUTO, SCIENCE COLLECT, or SCIENCE TRANSMIT.");
                return false;
            }
            command.Mode = mode;
            return true;
        }

        private static bool ReadDeploy(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            bool solarPanels = tokens.Count == 3 &&
                               tokens[1].Equals("SOLAR", StringComparison.OrdinalIgnoreCase) &&
                               tokens[2].Equals("PANELS", StringComparison.OrdinalIgnoreCase);
            bool solar = tokens.Count == 2 &&
                         tokens[1].Equals("SOLAR", StringComparison.OrdinalIgnoreCase);
            if (!solarPanels && !solar)
            {
                errors.Add("Line " + command.Line + ": use DEPLOY SOLAR PANELS;.");
                return false;
            }
            return true;
        }

        private static bool ReadModule(List<string> tokens, ScriptCommand command, List<string> errors)
        {
            if (tokens.Count != 2 || !Modules.Contains(tokens[1]))
            {
                errors.Add("Line " + command.Line + ": unknown module. Available modules are AUTOSTAGE, MANUALSTAGE, CTRLOCK, CLEARSKIES, AUTOWARP, MAXSCIENCE, SAFEABORT, SMARTRCS, AUTOLAND, RENDEZVOUS, DOCKING, and RECOVERY.");
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
            if (tokens.Count != 4 || tokens[2] != "=")
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

        private static bool RequireTokenCount(List<string> tokens, int expected, ScriptCommand command, List<string> errors, string usage)
        {
            if (tokens.Count == expected)
                return true;
            errors.Add("Line " + command.Line + ": " + usage);
            return false;
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
            string metricName = tokens[start].ToUpperInvariant();
            if (metricName == "ORBIT")
            {
                string orbitState = tokens[start + 2].ToUpperInvariant();
                if (conditionOperator != ConditionOperator.Equal && conditionOperator != ConditionOperator.NotEqual)
                {
                    errors.Add("Line " + command.Line + ": ORBIT accepts only =, ==, or != with SAFE or UNSAFE.");
                    return false;
                }
                if (orbitState != "SAFE" && orbitState != "UNSAFE")
                {
                    errors.Add("Line " + command.Line + ": ORBIT state must be SAFE or UNSAFE.");
                    return false;
                }

                command.Condition = new Condition
                {
                    IsOrbitState = true,
                    OrbitSafe = orbitState == "SAFE",
                    Operator = conditionOperator,
                    Negated = conditionOperator == ConditionOperator.NotEqual
                };
                return true;
            }
            bool distanceMetric = metricName == "ALTITUDE" || metricName == "APOAPSIS" || metricName == "PERIAPSIS";
            if (distanceMetric)
            {
                if (!TryParseDistance(tokens[start + 2], out number))
                {
                    errors.Add("Line " + command.Line + ": condition value must be numeric or use a distance such as 100KM.");
                    return false;
                }
            }
            else if (!float.TryParse(tokens[start + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                errors.Add("Line " + command.Line + ": condition value must be numeric.");
                return false;
            }

            if (!Metrics.Contains(metricName))
            {
                errors.Add("Line " + command.Line + ": unknown telemetry name '" + tokens[start] + "'.");
                return false;
            }

            command.Condition = new Condition
            {
                Name = metricName,
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
