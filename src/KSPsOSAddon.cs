using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KSPSOS
{
    // Register for every scene and gate the UI/runtime ourselves.  This keeps
    // the terminal alive through save loading and scene transitions instead of
    // relying on Flight's one-shot scene callback to discover the addon.
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public sealed class KSPsOSAddon : MonoBehaviour
    {
        private readonly FlightScriptRuntime runtime = new FlightScriptRuntime();
        private Rect windowRect = new Rect(20f, 80f, 510f, 760f);
        private Vector2 editorScroll;
        private Vector2 syntaxScroll;
        private Vector2 logScroll;
        private string script = DefaultScript;
        private bool visible;
        private bool compactTerminal;
        private bool focusProgramEditor;
        private bool programEditorActive;
        private int programCursor;
        private int lastF8ToggleFrame = -1;
        private bool terminalControlLockActive;
        private string scriptsDirectory;
        private string currentScriptPath;
        private string scriptStorageStatus = "Not saved yet";
        private string feasibilityReport = "Press CHECK IF POSSIBLE to inspect the active vessel.";
        private int reuseIndex = -1;
        private GUIStyle wrappedLabel;
        private GUIStyle richLabel;
        private GUIStyle errorLabel;
        private GUIStyle programEditorStyle;
        private GUIStyle titleStyle;
        private GUIStyle sectionStyle;
        private GUIStyle hintStyle;
        private GUIStyle compactButtonStyle;
        private Vessel subscribedVessel;
        private const string EditorIndent = "    ";
        private const string TerminalControlLockId = "KSPsOSTerminal";

        private const string DefaultScript = "# KSP-sOS example\nLAUNCH;\nHOLD PG 10;\nSTAGE;\nHOLD 5;\nCIRC;";

        private void Awake()
        {
            runtime.SetVessel(FlightGlobals.ActiveVessel);
            scriptsDirectory = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPsOS", "PluginData", "Scripts");
            EnsureScriptDirectory();
            Debug.Log("[KSP-sOS] Addon loaded in " + HighLogic.LoadedScene + ". Press F8 in Flight to open the terminal.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                ToggleTerminal();

            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                visible = false;
                programEditorActive = false;
                SetTerminalControlLock(false);
                return;
            }

            SetTerminalControlLock(runtime.RequestsControlLock);

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
            try
            {
                runtime.Tick(Time.deltaTime);
            }
            catch (Exception exception)
            {
                Debug.LogError("[KSP-sOS] Flight script failed: " + exception);
                runtime.Stop("Script stopped after an error. Check KSP.log.");
            }
        }

        private void OnDestroy()
        {
            SetTerminalControlLock(false);
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
            // Input.GetKeyDown is normally sufficient, but KSP's input locks
            // can prevent it from seeing a key on the same frame.  Mirror the
            // terminal's raw-event fallback while de-duplicating Update() so
            // F8 never opens and closes the window in one frame.
            if (HighLogic.LoadedScene == GameScenes.FLIGHT &&
                Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.F8)
            {
                ToggleTerminal();
                Event.current.Use();
                return;
            }

            if (!visible || HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            // The terminal itself remains non-blocking so the player can look around
            // and pause.  `using ctrlock;` opts a running script into vessel control lock.
            SetTerminalControlLock(runtime.RequestsControlLock);
            GUI.skin = HighLogic.Skin;

            if (Event.current.type == EventType.MouseDown && !windowRect.Contains(Event.current.mousePosition))
            {
                programEditorActive = false;
            }
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 &&
                windowRect.Contains(Event.current.mousePosition))
            {
                compactTerminal = !compactTerminal;
                Event.current.Use();
                return;
            }

            // Process editor keys before GUI.Window can route them to another IMGUI control.
            HandleProgramKeyboard();

            if (wrappedLabel == null)
            {
                wrappedLabel = new GUIStyle(GUI.skin.label);
                wrappedLabel.wordWrap = true;
                richLabel = new GUIStyle(wrappedLabel);
                richLabel.richText = true;
                errorLabel = new GUIStyle(richLabel);
                errorLabel.normal.textColor = new Color(1f, 0.35f, 0.35f);
                errorLabel.fontStyle = FontStyle.Bold;
                programEditorStyle = new GUIStyle(GUI.skin.textArea);
                programEditorStyle.wordWrap = false;
                titleStyle = new GUIStyle(GUI.skin.label);
                titleStyle.fontSize = 16;
                titleStyle.fontStyle = FontStyle.Bold;
                titleStyle.normal.textColor = new Color(0.45f, 0.85f, 1f);
                sectionStyle = new GUIStyle(GUI.skin.box);
                sectionStyle.fontStyle = FontStyle.Bold;
                sectionStyle.alignment = TextAnchor.MiddleLeft;
                sectionStyle.normal.textColor = new Color(0.75f, 0.9f, 1f);
                hintStyle = new GUIStyle(wrappedLabel);
                hintStyle.fontSize = 11;
                hintStyle.normal.textColor = new Color(0.7f, 0.75f, 0.8f);
                compactButtonStyle = new GUIStyle(GUI.skin.button);
                compactButtonStyle.fontSize = 11;
            }

            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "KSP-sOS Terminal");
        }

        private void ToggleTerminal()
        {
            if (lastF8ToggleFrame == Time.frameCount)
                return;

            lastF8ToggleFrame = Time.frameCount;
            visible = !visible;
            focusProgramEditor = visible;
            programEditorActive = visible;
            if (!visible)
                programEditorActive = false;
        }

        private void SetTerminalControlLock(bool locked)
        {
            if (locked == terminalControlLockActive)
                return;

            if (locked)
                InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, TerminalControlLockId);
            else
                InputLockManager.RemoveControlLock(TerminalControlLockId);

            terminalControlLockActive = locked;
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("KSP-sOS", titleStyle);
            GUILayout.Label("Write one mission here, save it explicitly, then run it. F8 hides it; right-click toggles compact mode.", hintStyle);

            GUILayout.Label("Mission script", sectionStyle);
            GUILayout.Label("Script file: " + scriptStorageStatus, wrappedLabel);
            editorScroll = GUILayout.BeginScrollView(editorScroll, GUILayout.Height(compactTerminal ? 150f : 190f));
            if (focusProgramEditor)
            {
                focusProgramEditor = false;
                programEditorActive = true;
            }
            programCursor = Mathf.Clamp(programCursor, 0, script.Length);
            string displayedScript = programEditorActive ? script.Insert(programCursor, "|") : script;
            GUILayout.Box(displayedScript, programEditorStyle, GUILayout.MinHeight(180f), GUILayout.ExpandWidth(true));
            Rect programEditorRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown)
            {
                if (programEditorRect.Contains(Event.current.mousePosition))
                {
                    programEditorActive = true;
                    programCursor = script.Length;
                    GUI.FocusControl(string.Empty);
                }
                else
                    programEditorActive = false;
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("RUN PROGRAM", GUILayout.Height(30f)))
                runtime.Run(script);
            if (GUILayout.Button("CHECK IF POSSIBLE", GUILayout.Height(30f)))
                feasibilityReport = CheckIfPossible(script);
            if (GUILayout.Button("SAVE SCRIPT", GUILayout.Height(30f)))
                SaveCurrentScript();
            if (GUILayout.Button("STOP", GUILayout.Height(30f)))
                runtime.Stop("Stopped by user.");
            if (GUILayout.Button("CLEAR LOG", GUILayout.Height(30f)))
                runtime.ClearLog();
            GUILayout.EndHorizontal();
            GUILayout.Label(feasibilityReport, wrappedLabel);

            if (!compactTerminal)
            {
                GUILayout.Label("Mission templates", sectionStyle);
                GUILayout.Label("Choose a starter program. Replace placeholder body names before running.", hintStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("LAUNCH + ORBIT", compactButtonStyle, GUILayout.Height(25f)))
                    LoadMissionTemplate("LaunchOrbit", "# Simple launch to orbit\nLAUNCH;\nHOLD PG 10;\nSTAGE;\nHOLD 5;\nCIRC;\n");
                if (GUILayout.Button("TRANSFER + LAND", compactButtonStyle, GUILayout.Height(25f)))
                    LoadMissionTemplate("TransferLand", "# Replace MUN with the destination body\nHOHMANN TO MUN;\nEXEC;\nLAND ON MUN;\nRECOVER;\n");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("RENDEZVOUS + DOCK", compactButtonStyle, GUILayout.Height(25f)))
                    LoadMissionTemplate("Docking", "# Select a target vessel in KSP first\nDISTANCE 100;\nRENDEZVOUS WITH TARGET;\nAUTODOCK;\n");
                if (GUILayout.Button("CLEAR PROGRAM", compactButtonStyle, GUILayout.Height(25f)))
                    LoadMissionTemplate("NewScript", string.Empty);
                GUILayout.EndHorizontal();

                GUILayout.Label("Syntax preview", sectionStyle);
                GUILayout.Label("<color=#C586C0>logic</color>  <color=#DCDCAA>flight/orbit</color>  <color=#569CD6>vessel</color>  <color=#4FC1FF>science</color>  <color=#4EC9B0>mission</color>  <color=#D4D4D4>general</color>  <color=#F44747>unknown</color>  <color=#B5CEA8>numbers</color>  <color=#6A9955>comments</color>", richLabel);
                syntaxScroll = GUILayout.BeginScrollView(syntaxScroll, GUILayout.Height(115f));
                string[] previewLines = script.Replace("\r", "").Split('\n');
                for (int i = 0; i < previewLines.Length; i++)
                    GUILayout.Label(HighlightLine(previewLines[i]), richLabel);
                GUILayout.EndScrollView();
            }

            var parseErrors = new List<string>();
            CommandParser.Parse(script, parseErrors);
            for (int i = 0; i < parseErrors.Count; i++)
                GUILayout.Label(parseErrors[i], errorLabel);

            GUILayout.Label("Status: " + runtime.Status, wrappedLabel);
            if (!compactTerminal)
            {
                GUILayout.Label("Terminal output", sectionStyle);
                logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(190f));
                for (int i = 0; i < runtime.Log.Count; i++)
                    GUILayout.Label(runtime.Log[i], wrappedLabel);
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Commands: LAUNCH, STAGE, HOLD, CIRC, THROTTLE, SAS, ABORT, IF, WHEN, SET", wrappedLabel);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("WRITE NEW SCRIPT", GUILayout.Height(28f)))
                    WriteNewScript();
                if (GUILayout.Button("REUSE SCRIPT", GUILayout.Height(28f)))
                    ReuseScript();
                GUILayout.EndHorizontal();
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 25f));
        }

        private void LoadMissionTemplate(string name, string template)
        {
            currentScriptPath = CreateScriptPath();
            script = template;
            programCursor = script.Length;
            reuseIndex = -1;
            scriptStorageStatus = string.IsNullOrEmpty(currentScriptPath)
                ? name + " (not saved)"
                : Path.GetFileName(currentScriptPath);
            focusProgramEditor = true;
            programEditorActive = true;
        }

        private string CheckIfPossible(string source)
        {
            var errors = new List<string>();
            List<ScriptCommand> commands = CommandParser.Parse(source, errors);
            if (errors.Count > 0)
                return "CHECK FAILED: " + string.Join(" | ", errors.ToArray());

            Vessel active = FlightGlobals.ActiveVessel;
            if (active == null)
                return "CHECK FAILED: no active vessel.";

            string report = "CHECK: " + active.vesselName + " | mass " + active.totalMass.ToString("0.00") + "t";
            if (active.VesselDeltaV == null || active.VesselDeltaV.OperatingStageInfo == null || active.VesselDeltaV.OperatingStageInfo.Count == 0)
                return report + "\nWARNING: KSP delta-v data is not ready.";

            double totalDv = 0d;
            float lowestTwr = float.MaxValue;
            for (int i = 0; i < active.VesselDeltaV.OperatingStageInfo.Count; i++)
            {
                DeltaVStageInfo stage = active.VesselDeltaV.OperatingStageInfo[i];
                totalDv += Math.Max(0d, stage.deltaVActual);
                lowestTwr = Math.Min(lowestTwr, stage.TWRActual);
                report += "\nStage " + stage.stage + ": dV " + stage.deltaVActual.ToString("0") + "m/s, TWR " + stage.TWRActual.ToString("0.00") + ", burn " + stage.stageBurnTime.ToString("0.0") + "s";
            }
            report += "\nTotal available dV: " + totalDv.ToString("0") + "m/s";
            if (lowestTwr < 1f)
                report += "\nWARNING: at least one stage has TWR below 1.0.";
            if (totalDv < 1000d)
                report += "\nWARNING: very low total dV for an orbital mission.";
            for (int i = 0; i < commands.Count; i++)
            {
                ScriptCommand command = commands[i];
                if (command.Kind == CommandKind.GravTurn && !string.IsNullOrEmpty(command.Body) && active.mainBody != null &&
                    !command.Body.Equals(active.mainBody.bodyName, StringComparison.OrdinalIgnoreCase))
                    report += "\nWARNING: GRAVTURN body " + command.Body + " does not match current body " + active.mainBody.bodyName + ".";
                if (command.Kind == CommandKind.Circ && active.orbit == null)
                    report += "\nWARNING: CIRC requires a usable orbit.";
                if ((command.Kind == CommandKind.ExecuteNode || command.Kind == CommandKind.Transfer || command.Kind == CommandKind.Hohmann) && active.patchedConicSolver == null)
                    report += "\nWARNING: maneuver planning is unavailable for this vessel.";
            }
            report += "\nCHECK COMPLETE: warnings are advisory; the rocket is not controlled.";
            return report;
        }

        private void HandleProgramKeyboard()
        {
            if (!programEditorActive || Event.current.type != EventType.KeyDown)
                return;

            Event key = Event.current;
            if (key.keyCode == KeyCode.V && (key.control || key.command))
            {
                string pasted = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(pasted))
                    InsertProgramText(pasted.Replace("\r\n", "\n").Replace('\r', '\n'));
                key.Use();
                return;
            }

            if (key.control || key.command || key.alt)
                return;

            switch (key.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    int lineStart = ProgramLineStart();
                    string precedingLine = script.Substring(lineStart, programCursor - lineStart);
                    string indentation = LeadingWhitespace(precedingLine);
                    // KSP ships an older Mono runtime that does not expose the
                    // parameterless String.TrimEnd() method shape.  Use the
                    // legacy char-array overload so the call resolves in-game.
                    if (precedingLine.TrimEnd(new[] { ' ', '\t', '\r', '\n' }).EndsWith("{", StringComparison.Ordinal))
                        indentation += EditorIndent;
                    InsertProgramText("\n" + indentation);
                    key.Use();
                    return;
                case KeyCode.Backspace:
                    if (programCursor > 0)
                    {
                        script = script.Remove(programCursor - 1, 1);
                        programCursor--;
                    }
                    key.Use();
                    return;
                case KeyCode.Delete:
                    if (programCursor < script.Length)
                    {
                        script = script.Remove(programCursor, 1);
                    }
                    key.Use();
                    return;
                case KeyCode.LeftArrow:
                    programCursor = Math.Max(0, programCursor - 1);
                    key.Use();
                    return;
                case KeyCode.RightArrow:
                    programCursor = Math.Min(script.Length, programCursor + 1);
                    key.Use();
                    return;
                case KeyCode.UpArrow:
                    MoveProgramCursorVertical(-1);
                    key.Use();
                    return;
                case KeyCode.DownArrow:
                    MoveProgramCursorVertical(1);
                    key.Use();
                    return;
                case KeyCode.Home:
                    programCursor = ProgramLineStart();
                    key.Use();
                    return;
                case KeyCode.End:
                    int lineEnd = script.IndexOf('\n', programCursor);
                    programCursor = lineEnd < 0 ? script.Length : lineEnd;
                    key.Use();
                    return;
                case KeyCode.Tab:
                    InsertProgramText(EditorIndent);
                    key.Use();
                    return;
                case KeyCode.Escape:
                    programEditorActive = false;
                    key.Use();
                    return;
            }

            char character = key.character;
            if (character >= ' ' && character < '\ue000' && character != '\u007f')
            {
                if (character == '}')
                {
                    int lineStart = ProgramLineStart();
                    string beforeCursor = script.Substring(lineStart, programCursor - lineStart);
                    if (beforeCursor.Trim().Length == 0 && beforeCursor.Length >= EditorIndent.Length)
                    {
                        script = script.Remove(programCursor - EditorIndent.Length, EditorIndent.Length);
                        programCursor -= EditorIndent.Length;
                    }
                }
                InsertProgramText(character.ToString());
                key.Use();
            }
        }

        private void InsertProgramText(string inserted)
        {
            script = script.Insert(programCursor, inserted);
            programCursor += inserted.Length;
        }

        private int ProgramLineStart()
        {
            return programCursor == 0 ? 0 : script.LastIndexOf('\n', programCursor - 1) + 1;
        }

        private void MoveProgramCursorVertical(int direction)
        {
            int currentStart = ProgramLineStart();
            int column = programCursor - currentStart;
            if (direction < 0)
            {
                if (currentStart == 0)
                    return;
                int previousEnd = currentStart - 1;
                int previousStart = previousEnd == 0 ? 0 : script.LastIndexOf('\n', previousEnd - 1) + 1;
                programCursor = previousStart + Math.Min(column, previousEnd - previousStart);
            }
            else
            {
                int currentEnd = script.IndexOf('\n', programCursor);
                if (currentEnd < 0)
                    return;
                int nextStart = currentEnd + 1;
                int nextEnd = script.IndexOf('\n', nextStart);
                if (nextEnd < 0)
                    nextEnd = script.Length;
                programCursor = nextStart + Math.Min(column, nextEnd - nextStart);
            }
        }

        private void EnsureScriptDirectory()
        {
            if (string.IsNullOrEmpty(scriptsDirectory))
                return;
            try
            {
                Directory.CreateDirectory(scriptsDirectory);
            }
            catch (Exception exception)
            {
                scriptStorageStatus = "Storage unavailable: " + exception.Message;
            }
        }

        private void SaveCurrentScript()
        {
            if (string.IsNullOrEmpty(script) && string.IsNullOrEmpty(currentScriptPath))
                return;
            EnsureScriptDirectory();
            if (string.IsNullOrEmpty(currentScriptPath))
                currentScriptPath = CreateScriptPath();
            if (string.IsNullOrEmpty(currentScriptPath))
                return;

            try
            {
                File.WriteAllText(currentScriptPath, script ?? string.Empty);
                scriptStorageStatus = Path.GetFileName(currentScriptPath);
            }
            catch (Exception exception)
            {
                scriptStorageStatus = "Save failed: " + exception.Message;
            }
        }

        private string CreateScriptPath()
        {
            if (string.IsNullOrEmpty(scriptsDirectory))
                return null;

            string baseName = "Script_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(scriptsDirectory, baseName + ".kspsos");
            int suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(scriptsDirectory, baseName + "_" + suffix + ".kspsos");
                suffix++;
            }
            return path;
        }

        private string[] SavedScripts()
        {
            EnsureScriptDirectory();
            if (string.IsNullOrEmpty(scriptsDirectory))
                return new string[0];
            try
            {
                string[] files = Directory.GetFiles(scriptsDirectory, "*.kspsos");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch (Exception exception)
            {
                scriptStorageStatus = "Library unavailable: " + exception.Message;
                return new string[0];
            }
        }

        private void WriteNewScript()
        {
            currentScriptPath = CreateScriptPath();
            script = string.Empty;
            programCursor = 0;
            reuseIndex = -1;
            scriptStorageStatus = string.IsNullOrEmpty(currentScriptPath)
                ? "New script (not saved)"
                : Path.GetFileName(currentScriptPath);
            focusProgramEditor = true;
            programEditorActive = true;
        }

        private void ReuseScript()
        {
            string[] files = SavedScripts();
            if (files.Length == 0)
            {
                scriptStorageStatus = "No saved scripts found";
                return;
            }

            if (reuseIndex < 0 || reuseIndex >= files.Length)
            {
                int currentIndex = Array.IndexOf(files, currentScriptPath);
                reuseIndex = currentIndex >= 0
                    ? (currentIndex - 1 + files.Length) % files.Length
                    : files.Length - 1;
            }
            else
                reuseIndex = (reuseIndex - 1 + files.Length) % files.Length;

            try
            {
                currentScriptPath = files[reuseIndex];
                script = File.ReadAllText(currentScriptPath);
                programCursor = script.Length;
                scriptStorageStatus = Path.GetFileName(currentScriptPath);
                focusProgramEditor = true;
                programEditorActive = true;
            }
            catch (Exception exception)
            {
                scriptStorageStatus = "Load failed: " + exception.Message;
            }
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
                escaped = "<color=#FFD700>" + escaped + "</color>";
            else
            {
                Match first = Regex.Match(code, @"^\s*([A-Za-z_*]+)");
                if (first.Success)
                {
                    string word = first.Groups[1].Value;
                    string upper = word.ToUpperInvariant();
                    string color = SyntaxColor(upper) ?? "#F44747";
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
            escaped = Regex.Replace(escaped, @"(?<![A-Za-z0-9_#])(-?\d+(?:\.\d+)?)(?![A-Za-z0-9_])", "<color=#B5CEA8>$1</color>");
            escaped = Regex.Replace(escaped, @"""([^""]*)""", "<color=#CE9178>\"$1\"</color>");
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
                    return "#C586C0";
                case "CIRC":
                case "CIRCULARIZE":
                case "CIRCULARISE":
                case "GRAVTURN":
                case "KEEP":
                case "SPEED":
                case "SURFACE":
                case "ORBITAL":
                case "VERTICAL":
                case "POLAR":
                case "EQUATORIAL":
                case "HOHMANN":
                case "TRANSFER":
                case "EXECUTE":
                case "ALIGN":
                case "THROTTLE":
                    return "#DCDCAA";
                case "LAUNCH":
                case "STAGE":
                case "HOLD":
                case "WAIT":
                case "SAS":
                case "RCS":
                case "ABORT":
                case "DEPLOY":
                    return "#569CD6";
                case "SCIENCE":
                case "DO":
                case "AUTO":
                case "COLLECT":
                case "TRANSMIT":
                case "MAXSCIENCE":
                    return "#4FC1FF";
                case "AUTOSTAGE":
                    return "#569CD6";
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
                    return "#4EC9B0";
                case "PRINT":
                    return "#DCDCAA";
                default:
                    return null;
            }
        }

        private static string EscapeRichText(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string LeadingWhitespace(string text)
        {
            int count = 0;
            while (count < text.Length && (text[count] == ' ' || text[count] == '\t'))
                count++;
            return text.Substring(0, count);
        }
    }
}
