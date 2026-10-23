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
            WaitingForCondition,
            GravityTurning
        }

        private sealed class GravityTurnProfile
        {
            public string Body;
            public double TurnStartAltitude;
            public double TurnEndAltitude;
            public double InitialPitchSeconds;
            public float InitialPitchInput;
            public double ProgradeSpeed;
            public double TargetApoapsis;
        }

        private readonly List<string> log = new List<string>();
        private readonly Dictionary<string, bool> flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<ScriptCommand> commands = new List<ScriptCommand>();
        private int commandIndex;
        private float holdRemaining;
        private RuntimeState state = RuntimeState.Idle;
        private Vessel vessel;
        private bool circularBurnStarted;
        private float throttle;
        private float lastAutoStageTime = -100f;
        private GravityTurnProfile gravityTurnProfile;
        private double gravityTurnElapsed;
        private bool gravityTurnPrograde;
        private float pitchInput;

        public float Throttle { get { return throttle; } }
        public float PitchInput { get { return pitchInput; } }
        public bool IsControllingAttitude { get { return state == RuntimeState.GravityTurning; } }
        public bool IsRunning { get { return state != RuntimeState.Idle || commandIndex < commands.Count; } }
        public bool IsControllingThrottle { get; private set; }
        public string Status { get; private set; }
        public IReadOnlyList<string> Log { get { return log; } }

        public FlightScriptRuntime()
        {
            Status = "Ready. Press RUN to execute the script.";
            throttle = 0f;
            pitchInput = 0f;
            IsControllingThrottle = false;
        }

        public void SetVessel(Vessel activeVessel)
        {
            vessel = activeVessel;
        }

        public void Run(string script)
        {
            Stop("Stopped.");
            modules.Clear();
            flags.Clear();
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
            gravityTurnProfile = null;
            gravityTurnElapsed = 0d;
            gravityTurnPrograde = false;
            pitchInput = 0f;
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
            pitchInput = 0f;
            gravityTurnProfile = null;
            gravityTurnPrograde = false;
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

            if (HasModule("AUTOSTAGE"))
                ApplyAutoStage();

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
            else if (state == RuntimeState.GravityTurning)
            {
                TickGravityTurn(deltaTime);
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

                case CommandKind.Rcs:
                    SetRcs(command.Mode);
                    commandIndex++;
                    return true;

                case CommandKind.Circ:
                    SetAutopilot(VesselAutopilot.AutopilotMode.Prograde);
                    throttle = 0f;
                    circularBurnStarted = false;
                    state = RuntimeState.WaitingForApoapsis;
                    Status = "Waiting for apoapsis to circularize.";
                    return true;

                case CommandKind.GravTurn:
                    gravityTurnProfile = ProfileForBody(command.Body);
                    gravityTurnElapsed = 0d;
                    gravityTurnPrograde = false;
                    pitchInput = 0f;
                    throttle = 1f;
                    SetAutopilot(VesselAutopilot.AutopilotMode.StabilityAssist);
                    state = RuntimeState.GravityTurning;
                    Status = "Gravity turn profile loaded for " + gravityTurnProfile.Body + ".";
                    AddLog("GRAVTURN " + gravityTurnProfile.Body + ": target apoapsis " + gravityTurnProfile.TargetApoapsis.ToString("0") + "m.");
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

                case CommandKind.Using:
                    modules.Add(command.Module);
                    AddLog("USING " + command.Module + ".");
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

        private void TickGravityTurn(float deltaTime)
        {
            if (vessel == null || gravityTurnProfile == null)
                return;

            string currentBody = vessel.mainBody == null ? string.Empty : vessel.mainBody.bodyName.ToUpperInvariant();
            if (currentBody != gravityTurnProfile.Body)
            {
                pitchInput = 0f;
                throttle = 0f;
                Stop("GRAVTURN body mismatch: profile " + gravityTurnProfile.Body + ", current body " + currentBody + ".");
                AddLog("GRAVTURN stopped because the selected body does not match the current vessel body.");
                return;
            }

            double altitude = Math.Max(0d, vessel.altitude);
            double speed = Math.Max(0d, vessel.srfSpeed);
            double apoapsis = vessel.orbit == null ? 0d : vessel.orbit.ApA;

            if (altitude < gravityTurnProfile.TurnStartAltitude)
            {
                pitchInput = 0f;
                throttle = 1f;
                return;
            }

            gravityTurnElapsed += Math.Max(0d, deltaTime);

            if (!gravityTurnPrograde &&
                (gravityTurnElapsed >= gravityTurnProfile.InitialPitchSeconds ||
                 altitude >= gravityTurnProfile.TurnEndAltitude) &&
                speed >= gravityTurnProfile.ProgradeSpeed)
            {
                gravityTurnPrograde = true;
                pitchInput = 0f;
                SetAutopilot(VesselAutopilot.AutopilotMode.Prograde);
                AddLog("GRAVTURN: prograde guidance engaged.");
            }
            else if (!gravityTurnPrograde)
            {
                double pitchFraction = gravityTurnProfile.InitialPitchSeconds <= 0d
                    ? 1d
                    : Math.Min(1d, gravityTurnElapsed / gravityTurnProfile.InitialPitchSeconds);
                pitchInput = gravityTurnProfile.InitialPitchInput * (float)(1d - pitchFraction);
                throttle = 1f;
            }

            if (apoapsis >= gravityTurnProfile.TargetApoapsis)
            {
                pitchInput = 0f;
                throttle = 0f;
                state = RuntimeState.Idle;
                gravityTurnProfile = null;
                commandIndex++;
                Status = "Gravity turn complete.";
                AddLog("GRAVTURN complete.");
                return;
            }

            if (apoapsis > gravityTurnProfile.TargetApoapsis * 0.9)
                throttle = 0.35f;
            else
                throttle = 1f;
        }

        private static GravityTurnProfile ProfileForBody(string body)
        {
            switch (body)
            {
                case "KERBIN": return new GravityTurnProfile { Body = "KERBIN", TurnStartAltitude = 100, TurnEndAltitude = 45000, InitialPitchSeconds = 8, InitialPitchInput = -0.12f, ProgradeSpeed = 45, TargetApoapsis = 80000 };
                case "DUNA": return new GravityTurnProfile { Body = "DUNA", TurnStartAltitude = 50, TurnEndAltitude = 30000, InitialPitchSeconds = 10, InitialPitchInput = -0.08f, ProgradeSpeed = 35, TargetApoapsis = 70000 };
                case "EVE": return new GravityTurnProfile { Body = "EVE", TurnStartAltitude = 250, TurnEndAltitude = 50000, InitialPitchSeconds = 14, InitialPitchInput = -0.06f, ProgradeSpeed = 60, TargetApoapsis = 90000 };
                case "LAYTHE": return new GravityTurnProfile { Body = "LAYTHE", TurnStartAltitude = 100, TurnEndAltitude = 35000, InitialPitchSeconds = 9, InitialPitchInput = -0.10f, ProgradeSpeed = 45, TargetApoapsis = 70000 };
                case "JUPITER":
                case "JOOL": return new GravityTurnProfile { Body = body, TurnStartAltitude = 300, TurnEndAltitude = 50000, InitialPitchSeconds = 12, InitialPitchInput = -0.06f, ProgradeSpeed = 70, TargetApoapsis = 120000 };
                case "MUN": return new GravityTurnProfile { Body = "MUN", TurnStartAltitude = 20, TurnEndAltitude = 3000, InitialPitchSeconds = 3, InitialPitchInput = -0.18f, ProgradeSpeed = 15, TargetApoapsis = 20000 };
                case "MINMUS": return new GravityTurnProfile { Body = "MINMUS", TurnStartAltitude = 10, TurnEndAltitude = 2500, InitialPitchSeconds = 2, InitialPitchInput = -0.20f, ProgradeSpeed = 10, TargetApoapsis = 15000 };
                case "GILLY": return new GravityTurnProfile { Body = "GILLY", TurnStartAltitude = 5, TurnEndAltitude = 500, InitialPitchSeconds = 1.5, InitialPitchInput = -0.22f, ProgradeSpeed = 5, TargetApoapsis = 10000 };
                case "IKE": return new GravityTurnProfile { Body = "IKE", TurnStartAltitude = 15, TurnEndAltitude = 2500, InitialPitchSeconds = 3, InitialPitchInput = -0.18f, ProgradeSpeed = 15, TargetApoapsis = 20000 };
                case "VALL": return new GravityTurnProfile { Body = "VALL", TurnStartAltitude = 20, TurnEndAltitude = 3000, InitialPitchSeconds = 3, InitialPitchInput = -0.18f, ProgradeSpeed = 15, TargetApoapsis = 25000 };
                case "TYLO": return new GravityTurnProfile { Body = "TYLO", TurnStartAltitude = 30, TurnEndAltitude = 5000, InitialPitchSeconds = 4, InitialPitchInput = -0.16f, ProgradeSpeed = 25, TargetApoapsis = 30000 };
                case "DRES": return new GravityTurnProfile { Body = "DRES", TurnStartAltitude = 15, TurnEndAltitude = 2500, InitialPitchSeconds = 3, InitialPitchInput = -0.18f, ProgradeSpeed = 15, TargetApoapsis = 20000 };
                case "BOP": return new GravityTurnProfile { Body = "BOP", TurnStartAltitude = 5, TurnEndAltitude = 800, InitialPitchSeconds = 1.5, InitialPitchInput = -0.22f, ProgradeSpeed = 5, TargetApoapsis = 12000 };
                case "POL": return new GravityTurnProfile { Body = "POL", TurnStartAltitude = 5, TurnEndAltitude = 800, InitialPitchSeconds = 1.5, InitialPitchInput = -0.22f, ProgradeSpeed = 5, TargetApoapsis = 12000 };
                case "MOHO": return new GravityTurnProfile { Body = "MOHO", TurnStartAltitude = 50, TurnEndAltitude = 15000, InitialPitchSeconds = 8, InitialPitchInput = -0.10f, ProgradeSpeed = 35, TargetApoapsis = 40000 };
                case "ELOO": return new GravityTurnProfile { Body = "ELOO", TurnStartAltitude = 15, TurnEndAltitude = 3000, InitialPitchSeconds = 3, InitialPitchInput = -0.18f, ProgradeSpeed = 15, TargetApoapsis = 25000 };
                default: return new GravityTurnProfile { Body = body, TurnStartAltitude = 100, TurnEndAltitude = 30000, InitialPitchSeconds = 8, InitialPitchInput = -0.10f, ProgradeSpeed = 35, TargetApoapsis = 50000 };
            }
        }

        private void Stage()
        {
            if (vessel != null)
                KSP.UI.Screens.StageManager.ActivateNextStage();
        }

        private bool HasModule(string module)
        {
            return modules.Contains(module);
        }

        private void SetRcs(string action)
        {
            if (vessel == null || vessel.ActionGroups == null)
                return;

            if (action == "TOGGLE")
                vessel.ActionGroups.ToggleGroup(KSPActionGroup.RCS);
            else
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, action == "ON");
            AddLog("RCS " + action + ".");
        }

        private void ApplyAutoStage()
        {
            if (vessel == null || throttle < 0.05f || vessel.currentStage <= 0)
                return;
            if (Time.time - lastAutoStageTime < 1f)
                return;

            bool hasLiquidFuel;
            bool hasOxidizer;
            bool hasSolidFuel;
            double liquidFuel = ReadStageResource("LiquidFuel", out hasLiquidFuel);
            double oxidizer = ReadStageResource("Oxidizer", out hasOxidizer);
            double solidFuel = ReadStageResource("SolidFuel", out hasSolidFuel);

            bool liquidStageEmpty = (hasLiquidFuel || hasOxidizer) && liquidFuel <= 0.1 && oxidizer <= 0.1;
            bool solidStageEmpty = hasSolidFuel && solidFuel <= 0.1;
            if (!liquidStageEmpty && !solidStageEmpty)
                return;

            Stage();
            lastAutoStageTime = Time.time;
            AddLog("AUTOSTAGE: current stage resources depleted.");
        }

        private double ReadStageResource(string resourceName, out bool found)
        {
            found = false;
            double total = 0d;
            if (vessel.parts == null)
                return total;

            foreach (Part part in vessel.parts)
            {
                if (part.inverseStage != vessel.currentStage || part.Resources == null)
                    continue;
                foreach (PartResource resource in part.Resources)
                {
                    if (resource.resourceName == resourceName)
                    {
                        found = true;
                        total += resource.amount;
                    }
                }
            }
            return total;
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
