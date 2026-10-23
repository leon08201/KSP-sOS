using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPSOS
{
    internal sealed class FlightScriptRuntime
    {
        private enum RuntimeState
        {
            Idle,
            Holding,
            WaitingForApoapsis,
            Circularizing,
            WaitingForCondition
        }

        private readonly List<string> log = new List<string>();
        private readonly Dictionary<string, bool> flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private List<ScriptCommand> commands = new List<ScriptCommand>();
        private int commandIndex;
        private float holdRemaining;
        private RuntimeState state = RuntimeState.Idle;
        private Vessel vessel;
        private bool circularBurnStarted;
        private float throttle;

        public float Throttle { get { return throttle; } }
        public bool IsRunning { get { return state != RuntimeState.Idle || commandIndex < commands.Count; } }
        public bool IsControllingThrottle { get; private set; }
        public string Status { get; private set; }
        public IReadOnlyList<string> Log { get { return log; } }

        public FlightScriptRuntime()
        {
            Status = "Ready. Press RUN to execute the script.";
            throttle = 0f;
            IsControllingThrottle = false;
        }

        public void SetVessel(Vessel activeVessel)
        {
            vessel = activeVessel;
        }

        public void Run(string script)
        {
            Stop("Stopped.");
            var errors = new List<string>();
            commands = CommandParser.Parse(script, errors);
            if (errors.Count > 0)
            {
                foreach (string error in errors)
                    AddLog("ERROR: " + error);
                Status = "Script has errors.";
                return;
            }

            if (commands.Count == 0)
            {
                Status = "Nothing to run.";
                return;
            }

            commandIndex = 0;
            state = RuntimeState.Idle;
            circularBurnStarted = false;
            IsControllingThrottle = true;
            Status = "Running...";
            AddLog("Started " + commands.Count + " command(s).");
        }

        public void RunInteractiveLine(string line)
        {
            if (IsRunning)
            {
                Status = "Terminal busy. Press STOP before running a separate line.";
                AddLog("Terminal busy: line was not executed.");
                return;
            }

            Run(line);
        }

        public void Stop(string message)
        {
            state = RuntimeState.Idle;
            commandIndex = commands.Count;
            holdRemaining = 0f;
            circularBurnStarted = false;
            throttle = 0f;
            IsControllingThrottle = false;
            Status = message;
        }

        public void ClearLog()
        {
            log.Clear();
        }

        public void Tick(float deltaTime)
        {
            if (vessel == null || !IsRunning)
                return;

            if (state == RuntimeState.Holding)
            {
                holdRemaining -= Mathf.Max(0f, deltaTime);
                if (holdRemaining > 0f)
                    return;
                state = RuntimeState.Idle;
                commandIndex++;
            }
            else if (state == RuntimeState.WaitingForCondition)
            {
                if (!Evaluate(commands[commandIndex].Condition))
                    return;
                state = RuntimeState.Idle;
                commandIndex++;
            }
            else if (state == RuntimeState.WaitingForApoapsis || state == RuntimeState.Circularizing)
            {
                TickCircularization();
                return;
            }

            while (commandIndex < commands.Count && state == RuntimeState.Idle)
            {
                ScriptCommand command = commands[commandIndex];
                if (!Execute(command))
                    return;
            }

            if (commandIndex >= commands.Count && state == RuntimeState.Idle)
            {
                IsControllingThrottle = false;
                Status = "Finished.";
                AddLog("Script finished.");
            }
        }

        private bool Execute(ScriptCommand command)
        {
            switch (command.Kind)
            {
                case CommandKind.Launch:
                    throttle = 1f;
                    SetAutopilot(VesselAutopilot.AutopilotMode.Prograde);
                    Stage();
                    AddLog("LAUNCH: throttle 100%, prograde, stage.");
                    commandIndex++;
                    return true;

                case CommandKind.Stage:
                    Stage();
                    AddLog("STAGE.");
                    commandIndex++;
                    return true;

                case CommandKind.Hold:
                    if (!string.IsNullOrEmpty(command.Mode))
                        SetAutopilot(ToAutopilotMode(command.Mode));
                    holdRemaining = Mathf.Max(0f, command.Number);
                    state = RuntimeState.Holding;
                    Status = "Holding " + (string.IsNullOrEmpty(command.Mode) ? "current attitude" : command.Mode) + " for " + command.Number.ToString("0.##") + "s.";
                    return true;

                case CommandKind.Throttle:
                    throttle = Mathf.Clamp01(command.Number);
                    AddLog("THROTTLE " + (throttle * 100f).ToString("0.#") + "%.");
                    commandIndex++;
                    return true;

                case CommandKind.Sas:
                    SetAutopilot(ToAutopilotMode(command.Mode));
                    AddLog("SAS " + command.Mode + ".");
                    commandIndex++;
                    return true;

                case CommandKind.Circ:
                    SetAutopilot(VesselAutopilot.AutopilotMode.Prograde);
                    throttle = 0f;
                    circularBurnStarted = false;
                    state = RuntimeState.WaitingForApoapsis;
                    Status = "Waiting for apoapsis to circularize.";
                    return true;

                case CommandKind.Abort:
                    if (vessel != null && vessel.ActionGroups != null)
                        vessel.ActionGroups.ToggleGroup(KSPActionGroup.Abort);
                    AddLog("ABORT activated.");
                    Stop("Aborted.");
                    return false;

                case CommandKind.Print:
                    AddLog(command.Text);
                    commandIndex++;
                    return true;

                case CommandKind.If:
                    commandIndex = Evaluate(command.Condition) ? commandIndex + 1 : command.JumpIndex;
                    return true;

                case CommandKind.Else:
                    commandIndex = command.JumpIndex + 1;
                    return true;

                case CommandKind.End:
                    commandIndex++;
                    return true;

                case CommandKind.When:
                    if (Evaluate(command.Condition))
                    {
                        commandIndex++;
                        return true;
                    }
                    state = RuntimeState.WaitingForCondition;
                    Status = "Waiting for condition on line " + command.Line + ".";
                    return false;

                case CommandKind.Set:
                    flags[command.Variable] = command.BoolValue;
                    AddLog("SET " + command.Variable + " = " + (command.BoolValue ? "TRUE" : "FALSE") + ".");
                    commandIndex++;
                    return true;
            }

            return false;
        }

        private void TickCircularization()
        {
            if (vessel == null || vessel.orbit == null)
                return;

            Orbit orbit = vessel.orbit;
            double apoapsis = orbit.ApR;
            double periapsis = orbit.PeR;

            if (apoapsis <= 0 || periapsis <= 0)
            {
                throttle = 0f;
                Stop("Circularization unavailable: no stable orbit.");
                AddLog("CIRC failed: vessel has no usable orbit.");
                return;
            }

            if (periapsis >= apoapsis * 0.985)
            {
                throttle = 0f;
                state = RuntimeState.Idle;
                commandIndex++;
                Status = "Circular orbit reached.";
                AddLog("CIRC complete.");
                return;
            }

            if (state == RuntimeState.WaitingForApoapsis)
            {
                if (orbit.timeToAp > 1.0)
                    return;

                state = RuntimeState.Circularizing;
                circularBurnStarted = true;
                Status = "Burning prograde at apoapsis.";
                AddLog("CIRC burn started.");
            }

            if (circularBurnStarted)
                throttle = 0.75f;
        }

        private void Stage()
        {
            if (vessel != null)
                KSP.UI.Screens.StageManager.ActivateNextStage();
        }

        private void SetAutopilot(VesselAutopilot.AutopilotMode mode)
        {
            if (vessel == null || vessel.Autopilot == null)
                return;

            vessel.Autopilot.Enable();
            vessel.Autopilot.SetMode(mode);
        }

        private static VesselAutopilot.AutopilotMode ToAutopilotMode(string mode)
        {
            switch (mode)
            {
                case "RG": return VesselAutopilot.AutopilotMode.Retrograde;
                case "NML": return VesselAutopilot.AutopilotMode.Normal;
                case "ANML": return VesselAutopilot.AutopilotMode.Antinormal;
                case "RI": return VesselAutopilot.AutopilotMode.RadialIn;
                case "RO": return VesselAutopilot.AutopilotMode.RadialOut;
                default: return VesselAutopilot.AutopilotMode.Prograde;
            }
        }

        private bool Evaluate(Condition condition)
        {
            if (condition == null)
                return false;

            bool result;
            if (condition.IsBoolean)
            {
                if (string.IsNullOrEmpty(condition.Name))
                    result = condition.BooleanValue;
                else
                {
                    bool value;
                    result = flags.TryGetValue(condition.Name, out value) && value;
                }

                return condition.Negated ? !result : result;
            }

            double actual = ReadMetric(condition.Name);
            switch (condition.Operator)
            {
                case ConditionOperator.NotEqual: result = Math.Abs(actual - condition.Number) > 0.001; break;
                case ConditionOperator.Less: result = actual < condition.Number; break;
                case ConditionOperator.LessOrEqual: result = actual <= condition.Number; break;
                case ConditionOperator.Greater: result = actual > condition.Number; break;
                case ConditionOperator.GreaterOrEqual: result = actual >= condition.Number; break;
                default: result = Math.Abs(actual - condition.Number) <= 0.001; break;
            }

            return condition.Negated ? !result : result;
        }

        private double ReadMetric(string name)
        {
            if (vessel == null)
                return 0d;

            switch (name)
            {
                case "ALTITUDE": return vessel.altitude;
                case "SPEED": return vessel.srfSpeed;
                case "APOAPSIS": return vessel.orbit == null ? 0d : vessel.orbit.ApA;
                case "PERIAPSIS": return vessel.orbit == null ? 0d : vessel.orbit.PeA;
                case "FUEL": return ReadResource("LiquidFuel");
                case "OXIDIZER": return ReadResource("Oxidizer");
                case "BATTERY": return ReadResource("ElectricCharge");
                default: return 0d;
            }
        }

        private double ReadResource(string resourceName)
        {
            double total = 0d;
            if (vessel.parts == null)
                return total;

            foreach (Part part in vessel.parts)
            {
                if (part.Resources == null)
                    continue;
                foreach (PartResource resource in part.Resources)
                {
                    if (resource.resourceName == resourceName)
                        total += resource.amount;
                }
            }
            return total;
        }

        private void AddLog(string message)
        {
            log.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
            while (log.Count > 100)
                log.RemoveAt(0);
        }
    }
}
