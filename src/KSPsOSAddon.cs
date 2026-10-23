using UnityEngine;

namespace KSPSOS
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class KSPsOSAddon : MonoBehaviour
    {
        private readonly FlightScriptRuntime runtime = new FlightScriptRuntime();
        private Rect windowRect = new Rect(20f, 80f, 510f, 640f);
        private Vector2 editorScroll;
        private Vector2 logScroll;
        private string script = DefaultScript;
        private string terminalInput = string.Empty;
        private bool visible;
        private GUIStyle wrappedLabel;
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
        }

        private void OnGUI()
        {
            if (!visible || HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            if (wrappedLabel == null)
            {
                wrappedLabel = new GUIStyle(GUI.skin.label);
                wrappedLabel.wordWrap = true;
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
    }
}
