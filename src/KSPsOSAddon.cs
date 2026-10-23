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
        private string terminalInput = string.Empty;
        private int terminalCursor;
        private bool visible;
        private bool focusTerminalOnOpen;
        private bool focusProgramEditor;
        private bool terminalInputActive;
        private bool programEditorActive;
        private int programCursor;
        private int lastF8ToggleFrame = -1;
        private bool terminalControlLockActive;
        private Rect terminalInputRect;
        private string scriptsDirectory;
        private string currentScriptPath;
        private string scriptStorageStatus = "Not saved yet";
        private float scriptSaveDueTime = -1f;
        private int reuseIndex = -1;
        private GUIStyle wrappedLabel;
        private GUIStyle richLabel;
        private GUIStyle errorLabel;
        private GUIStyle terminalLineStyle;
        private GUIStyle programEditorStyle;
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
                terminalInputActive = false;
                programEditorActive = false;
                SetTerminalControlLock(false);
                return;
            }

            SetTerminalControlLock(visible);

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
            if (scriptSaveDueTime >= 0f && Time.time >= scriptSaveDueTime)
            {
                SaveCurrentScript();
                scriptSaveDueTime = -1f;
            }
        }

        private void OnDestroy()
        {
            SetTerminalControlLock(false);
            if (subscribedVessel != null)
                subscribedVessel.OnFlyByWire -= OnFlyByWire;
            SaveCurrentScript();
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

            // Match kOS's behavior: the active terminal owns keyboard/control input
            // while it is open, then releases it as soon as it is hidden.
            SetTerminalControlLock(true);
            GUI.skin = HighLogic.Skin;

            if (Event.current.type == EventType.MouseDown && !windowRect.Contains(Event.current.mousePosition))
            {
                terminalInputActive = false;
                programEditorActive = false;
            }

            // Process keys before GUI.Window can route them to another IMGUI control.
            HandleTerminalKeyboardFallback();
            HandleProgramKeyboard();

            if (wrappedLabel == null)
            {
                wrappedLabel = new GUIStyle(GUI.skin.label);
                wrappedLabel.wordWrap = true;
                richLabel = new GUIStyle(wrappedLabel);
                richLabel.richText = true;
                errorLabel = new GUIStyle(richLabel);
                errorLabel.normal.textColor = new Color(1f, 0.35f, 0.35f);
                terminalLineStyle = new GUIStyle(GUI.skin.box);
                terminalLineStyle.wordWrap = false;
                programEditorStyle = new GUIStyle(GUI.skin.textArea);
                programEditorStyle.wordWrap = false;
            }

            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "KSP-sOS Terminal");
        }

        private void ToggleTerminal()
        {
            if (lastF8ToggleFrame == Time.frameCount)
                return;

            lastF8ToggleFrame = Time.frameCount;
            visible = !visible;
            focusTerminalOnOpen = visible;
            terminalInputActive = visible;
            if (!visible)
                programEditorActive = false;
        }

        private void SetTerminalControlLock(bool locked)
        {
            if (locked == terminalControlLockActive)
                return;

            if (locked)
                InputLockManager.SetControlLock(ControlTypes.All, TerminalControlLockId);
            else
                InputLockManager.RemoveControlLock(TerminalControlLockId);

            terminalControlLockActive = locked;
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("KSP-sOS — simple operating system", wrappedLabel);
            GUILayout.Label("Type a command below and press ENTER, or edit a program and run it. F8 toggles the terminal.", wrappedLabel);

            GUILayout.Label("Terminal input", GUI.skin.box);
            GUILayout.BeginHorizontal();
            terminalCursor = Mathf.Clamp(terminalCursor, 0, terminalInput.Length);
            string displayedInput = terminalInputActive
                ? terminalInput.Insert(terminalCursor, "|")
                : terminalInput;
            GUILayout.Box("> " + displayedInput, terminalLineStyle, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            terminalInputRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown)
            {
                if (terminalInputRect.Contains(Event.current.mousePosition))
                {
                    terminalInputActive = true;
                    programEditorActive = false;
                    GUI.FocusControl(string.Empty);
                }
                else
                {
                    terminalInputActive = false;
                }
            }
            if (focusTerminalOnOpen)
            {
                focusTerminalOnOpen = false;
                terminalInputActive = true;
            }
            if (GUILayout.Button("EXECUTE", GUILayout.Width(90f), GUILayout.Height(28f)))
            {
                ExecuteTerminalLine();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Program", GUI.skin.box);
            GUILayout.Label("Script file: " + scriptStorageStatus, wrappedLabel);
            editorScroll = GUILayout.BeginScrollView(editorScroll, GUILayout.Height(190f));
            if (focusProgramEditor)
            {
                focusProgramEditor = false;
                programEditorActive = true;
                terminalInputActive = false;
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
                    terminalInputActive = false;
                    programCursor = script.Length;
                    GUI.FocusControl(string.Empty);
                }
                else
                    programEditorActive = false;
            }
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
            {
                SaveCurrentScript();
                runtime.Run(script);
            }
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

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("WRITE NEW SCRIPT", GUILayout.Height(28f)))
                WriteNewScript();
            if (GUILayout.Button("REUSE SCRIPT", GUILayout.Height(28f)))
                ReuseScript();
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
            terminalCursor = 0;
        }

        private void HandleTerminalKeyboardFallback()
        {
            // kOS processes terminal keys from the IMGUI event stream. Keep this
            // command line independent of Unity's text-field focus as well.
            if (!terminalInputActive || Event.current.type != EventType.KeyDown)
                return;

            if (Event.current.keyCode == KeyCode.V &&
                (Event.current.control || Event.current.command))
            {
                string pasted = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(pasted))
                {
                    pasted = pasted.Replace("\r", " ").Replace("\n", " ");
                    terminalInput = terminalInput.Insert(terminalCursor, pasted);
                    terminalCursor += pasted.Length;
                }
                Event.current.Use();
                return;
            }

            EventModifiers blockedModifiers = EventModifiers.Control | EventModifiers.Command | EventModifiers.Alt;
            if ((Event.current.modifiers & blockedModifiers) != 0)
                return;

            if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
            {
                ExecuteTerminalLine();
                Event.current.Use();
                return;
            }

            if (Event.current.keyCode == KeyCode.Backspace)
            {
                if (terminalCursor > 0)
                {
                    terminalInput = terminalInput.Remove(terminalCursor - 1, 1);
                    terminalCursor--;
                }
                Event.current.Use();
                return;
            }

            if (Event.current.keyCode == KeyCode.Delete)
            {
                if (terminalCursor < terminalInput.Length)
                    terminalInput = terminalInput.Remove(terminalCursor, 1);
                Event.current.Use();
                return;
            }

            if (Event.current.keyCode == KeyCode.LeftArrow || Event.current.keyCode == KeyCode.RightArrow ||
                Event.current.keyCode == KeyCode.Home || Event.current.keyCode == KeyCode.End)
            {
                switch (Event.current.keyCode)
                {
                    case KeyCode.LeftArrow: terminalCursor = Math.Max(0, terminalCursor - 1); break;
                    case KeyCode.RightArrow: terminalCursor = Math.Min(terminalInput.Length, terminalCursor + 1); break;
                    case KeyCode.Home: terminalCursor = 0; break;
                    case KeyCode.End: terminalCursor = terminalInput.Length; break;
                }
                Event.current.Use();
                return;
            }

            if (Event.current.keyCode == KeyCode.Escape)
            {
                terminalInputActive = false;
                GUI.FocusControl(string.Empty);
                Event.current.Use();
                return;
            }

            char character = Event.current.character;
            if (character >= ' ' && character < '\ue000' && character != '\u007f')
            {
                terminalInput = terminalInput.Insert(terminalCursor, character.ToString());
                terminalCursor++;
                Event.current.Use();
            }
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
                        QueueScriptSave();
                    }
                    key.Use();
                    return;
                case KeyCode.Delete:
                    if (programCursor < script.Length)
                    {
                        script = script.Remove(programCursor, 1);
                        QueueScriptSave();
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
            QueueScriptSave();
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

        private void QueueScriptSave()
        {
            EnsureScriptDirectory();
            if (string.IsNullOrEmpty(currentScriptPath))
                currentScriptPath = CreateScriptPath();
            scriptStorageStatus = Path.GetFileName(currentScriptPath) + " (saving...)";
            scriptSaveDueTime = Time.time + 0.5f;
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
            SaveCurrentScript();
            currentScriptPath = CreateScriptPath();
            script = string.Empty;
            programCursor = 0;
            reuseIndex = -1;
            scriptStorageStatus = string.IsNullOrEmpty(currentScriptPath)
                ? "New script (not saved)"
                : Path.GetFileName(currentScriptPath);
            focusProgramEditor = true;
            programEditorActive = true;
            terminalInputActive = false;
            SaveCurrentScript();
        }

        private void ReuseScript()
        {
            SaveCurrentScript();
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
                terminalInputActive = false;
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

        private static string LeadingWhitespace(string text)
        {
            int count = 0;
            while (count < text.Length && (text[count] == ' ' || text[count] == '\t'))
                count++;
            return text.Substring(0, count);
        }
    }
}
