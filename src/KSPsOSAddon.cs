using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KSPSOS
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class KSPsOSAddon : MonoBehaviour
    {
        private readonly FlightScriptRuntime runtime = new FlightScriptRuntime();
        private Rect windowRect = new Rect(20f, 80f, 510f, 760f);
        private Vector2 editorScroll;
        private Vector2 syntaxScroll;
        private Vector2 logScroll;
        private string script = DefaultScript;
        private string terminalInput = string.Empty;
        private bool visible;
        private GUIStyle wrappedLabel;
        private GUIStyle richLabel;
        private GUIStyle errorLabel;
        private Vessel subscribedVessel;

        private const string DefaultScript = "# KSP-sOS example\nLAUNCH;\nHOLD PG 10;\nSTAGE;\nHOLD 5;\nCIRC;";

        private void Awake()
        {
            runtime.SetVessel(FlightGlobals.ActiveVessel);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                visible = !visible;

            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (activeVessel != subscribedVessel)
            {
                if (subscribedVessel != null)
                    subscribedVessel.OnFlyByWire -= OnFlyByWire;
                subscribedVessel = activeVessel;
                if (subscribedVessel != null)
                    subscribedVessel.OnFlyByWire += OnFlyByWire;
            }
            runtime.SetVessel(activeVessel);
            runtime.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (subscribedVessel != null)
                subscribedVessel.OnFlyByWire -= OnFlyByWire;
            runtime.Stop("Stopped.");
        }

        private void OnFlyByWire(FlightCtrlState state)
        {
            if (runtime.IsControllingThrottle)
                state.mainThrottle = runtime.Throttle;
            if (runtime.IsControllingAttitude)
            {
                state.pitch = runtime.PitchInput;
                state.yaw = runtime.YawInput;
                state.roll = runtime.RollInput;
            }
            if (runtime.IsControllingTranslation)
            {
                state.X = runtime.TranslationX;
                state.Y = runtime.TranslationY;
                state.Z = runtime.TranslationZ;
            }
        }

        private void OnGUI()
        {
            if (!visible || HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            if (wrappedLabel == null)
            {
                wrappedLabel = new GUIStyle(GUI.skin.label);
                wrappedLabel.wordWrap = true;
                richLabel = new GUIStyle(wrappedLabel);
                richLabel.richText = true;
                errorLabel = new GUIStyle(richLabel);
                errorLabel.normal.textColor = new Color(1f, 0.35f, 0.35f);
            }

            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "KSP-sOS Terminal");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("KSP-sOS — simple operating system", wrappedLabel);
            GUILayout.Label("Type a command below and press ENTER, or edit a program and run it. F8 toggles the terminal.", wrappedLabel);

            GUILayout.Label("Terminal input", GUI.skin.box);
            GUI.SetNextControlName("KSPsOSTerminalInput");
            GUILayout.BeginHorizontal();
            terminalInput = GUILayout.TextField(terminalInput, GUILayout.Height(28f));
            bool enterPressed = Event.current.type == EventType.KeyDown &&
                                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                                GUI.GetNameOfFocusedControl() == "KSPsOSTerminalInput";
            if (GUILayout.Button("EXECUTE", GUILayout.Width(90f), GUILayout.Height(28f)) || enterPressed)
            {
                ExecuteTerminalLine();
                if (enterPressed)
                    Event.current.Use();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Program", GUI.skin.box);
            editorScroll = GUILayout.BeginScrollView(editorScroll, GUILayout.Height(190f));
            script = GUILayout.TextArea(script, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.Label("Syntax preview", GUI.skin.box);
            GUILayout.Label("<color=#C792EA>logic</color>  <color=#F78C6C>burns/orbits</color>  <color=#FFCB6B>vessel control</color>  <color=#82AAFF>science</color>  <color=#C3E88D>mission</color>  <color=#4FC3F7>general</color>  <color=#FF5370>unknown</color>", richLabel);
            syntaxScroll = GUILayout.BeginScrollView(syntaxScroll, GUILayout.Height(115f));
            string[] previewLines = script.Replace("\r", "").Split('\n');
            for (int i = 0; i < previewLines.Length; i++)
                GUILayout.Label(HighlightLine(previewLines[i]), richLabel);
            GUILayout.EndScrollView();

            var parseErrors = new List<string>();
            CommandParser.Parse(script, parseErrors);
            for (int i = 0; i < parseErrors.Count; i++)
                GUILayout.Label(parseErrors[i], errorLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("RUN PROGRAM", GUILayout.Height(30f)))
                runtime.Run(script);
            if (GUILayout.Button("STOP", GUILayout.Height(30f)))
                runtime.Stop("Stopped by user.");
            if (GUILayout.Button("CLEAR LOG", GUILayout.Height(30f)))
                runtime.ClearLog();
            GUILayout.EndHorizontal();

            GUILayout.Label("Status: " + runtime.Status, wrappedLabel);
            GUILayout.Label("Terminal output", GUI.skin.box);
            logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(190f));
            for (int i = 0; i < runtime.Log.Count; i++)
                GUILayout.Label(runtime.Log[i], wrappedLabel);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Commands: LAUNCH, STAGE, HOLD, CIRC, THROTTLE, SAS, ABORT, IF, WHEN, SET", wrappedLabel);
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 25f));
        }

        private void ExecuteTerminalLine()
        {
            string line = terminalInput.Trim();
            if (line.Length == 0)
                return;

            runtime.RunInteractiveLine(line);
            terminalInput = string.Empty;
        }

        private static string HighlightLine(string sourceLine)
        {
            if (string.IsNullOrEmpty(sourceLine))
                return " ";

            int commentIndex = sourceLine.IndexOf('#');
            string code = commentIndex >= 0 ? sourceLine.Substring(0, commentIndex) : sourceLine;
            string comment = commentIndex >= 0 ? sourceLine.Substring(commentIndex) : string.Empty;
            string escaped = EscapeRichText(code);

            if (Regex.IsMatch(code.Trim(), @"^[{}]"))
                escaped = "<color=#C792EA>" + escaped + "</color>";
            else
            {
                Match first = Regex.Match(code, @"^\s*([A-Za-z_*]+)");
                if (first.Success)
                {
                    string word = first.Groups[1].Value;
                    string upper = word.ToUpperInvariant();
                    string color = SyntaxColor(upper) ?? "#FF5370";
                    string highlightedWord = "<color=" + color + ">" + EscapeRichText(word) + "</color>";
                    int wordIndex = first.Groups[1].Index;
                    string rest = EscapeRichText(code.Substring(wordIndex + word.Length));
                    rest = Regex.Replace(rest, @"\b[A-Za-z_]+\b", match =>
                    {
                        string restColor = SyntaxColor(match.Value.ToUpperInvariant());
                        return restColor == null ? match.Value : "<color=" + restColor + ">" + match.Value + "</color>";
                    });
                    escaped = EscapeRichText(code.Substring(0, wordIndex)) + highlightedWord + rest;
                }
            }

            // Do not match the digits inside generated rich-text colour tags, such as #FF5370.
            escaped = Regex.Replace(escaped, @"(?<![A-Za-z0-9_#])(-?\d+(?:\.\d+)?)(?![A-Za-z0-9_])", "<color=#FFCB6B>$1</color>");
            if (!string.IsNullOrEmpty(comment))
                escaped += "<color=#6A9955>" + EscapeRichText(comment) + "</color>";
            return escaped;
        }

        private static string SyntaxColor(string word)
        {
            switch (word)
            {
                case "IF":
                case "ELSE":
                case "END":
                case "ENDIF":
                case "WHEN":
                case "SET":
                case "USING":
                case "TRUE":
                case "FALSE":
                case "NOT":
                case "ORBIT":
                case "SAFE":
                case "UNSAFE":
                    return "#C792EA";
                case "CIRC":
                case "CIRCULARIZE":
                case "CIRCULARISE":
                case "GRAVTURN":
                case "POLAR":
                case "EQUATORIAL":
                case "HOHMANN":
                case "TRANSFER":
                case "EXECUTE":
                case "ALIGN":
                case "THROTTLE":
                    return "#F78C6C";
                case "LAUNCH":
                case "STAGE":
                case "HOLD":
                case "WAIT":
                case "SAS":
                case "RCS":
                case "ABORT":
                case "DEPLOY":
                    return "#FFCB6B";
                case "SCIENCE":
                case "DO":
                case "AUTO":
                case "COLLECT":
                case "TRANSMIT":
                case "MAXSCIENCE":
                    return "#82AAFF";
                case "AUTOSTAGE":
                    return "#FFCB6B";
                case "RENDEZVOUS":
                case "DOCK":
                case "AUTODOCK":
                case "APOLLO":
                case "TRANSPOSE":
                case "LAND":
                case "RECOVER":
                case "TARGET":
                case "DISTANCE":
                case "WALK":
                case "*WALK":
                case "PARK":
                case "ON":
                case "OFF":
                case "TO":
                case "WITH":
                case "THE":
                case "CB":
                case "AROUND":
                case "CLEARSKIES":
                case "AUTOWARP":
                case "SAFEABORT":
                case "SMARTRCS":
                case "AUTOLAND":
                case "DOCKING":
                case "RECOVERY":
                case "SOLAR":
                case "PANELS":
                    return "#C3E88D";
                case "PRINT":
                    return "#4FC3F7";
                default:
                    return null;
            }
        }

        private static string EscapeRichText(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
