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
            GravityTurning,
            ProcessingScience,
            Rendezvousing,
            Docking,
            Landing,
            WaitingForRecovery,
            WaitingForBody,
            WaitingForManeuver,
            ExecutingManeuver
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
        private bool gravityTurnPolar;
        private float pitchInput;
        private float scienceTimer;
        private double circularTargetAltitude = -1d;
        private double dockingDistance = 50d;
        private Vessel targetVessel;
        private ModuleDockingNode ownDockingNode;
        private ModuleDockingNode targetDockingNode;
        private bool sameVesselDock;
        private bool dockingRequested;
        private bool automaticDocking;
        private string landingBody;
        private bool landingCaptureComplete;
        private bool landingDeorbitComplete;
        private float yawInput;
        private float rollInput;
        private float translationX;
        private float translationY;
        private float translationZ;
        private float lastClearSkiesTime = -100f;
        private bool missionAssist;
        private bool gravityCommandAlreadyAdvanced;
        private bool launchPlanePending;
        private ManeuverNode activeManeuverNode;
        private double maneuverBurnStartUT;
        private float maneuverBurnDuration;
        private float maneuverBurnElapsed;
        private bool maneuverCommandAlreadyAdvanced;
        private string pendingTransferBody;
        private ManeuverNode pendingTransferNode;
        private bool activeManeuverIsTransfer;
        private bool transferBurnCompleted;
        private string pendingPolarBody;

        public float Throttle { get { return throttle; } }
        public float PitchInput { get { return pitchInput; } }
        public float YawInput { get { return yawInput; } }
        public float RollInput { get { return rollInput; } }
        public float TranslationX { get { return translationX; } }
        public float TranslationY { get { return translationY; } }
        public float TranslationZ { get { return translationZ; } }
        public bool IsControllingAttitude { get { return state == RuntimeState.GravityTurning || state == RuntimeState.Docking || state == RuntimeState.Landing; } }
        public bool IsControllingTranslation { get { return state == RuntimeState.Rendezvousing || state == RuntimeState.Docking; } }
        public bool IsRunning { get { return state != RuntimeState.Idle || commandIndex < commands.Count; } }
        public bool IsControllingThrottle { get; private set; }
        public string Status { get; private set; }
        public IReadOnlyList<string> Log { get { return log; } }

        public FlightScriptRuntime()
        {
            Status = "Ready. Press RUN to execute the script.";
            throttle = 0f;
            pitchInput = 0f;
            scienceTimer = 0f;
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
            gravityTurnPolar = false;
            scienceTimer = 0f;
            pitchInput = 0f;
            yawInput = 0f;
            rollInput = 0f;
            translationX = 0f;
            translationY = 0f;
            translationZ = 0f;
            circularTargetAltitude = -1d;
            dockingRequested = false;
            automaticDocking = false;
            targetVessel = null;
            ownDockingNode = null;
            targetDockingNode = null;
            landingBody = null;
            landingCaptureComplete = false;
            landingDeorbitComplete = false;
            lastClearSkiesTime = -100f;
            missionAssist = false;
            gravityCommandAlreadyAdvanced = false;
            launchPlanePending = false;
            activeManeuverNode = null;
            maneuverBurnStartUT = 0d;
            maneuverBurnDuration = 0f;
            maneuverBurnElapsed = 0f;
            maneuverCommandAlreadyAdvanced = false;
            pendingTransferBody = null;
            pendingTransferNode = null;
            activeManeuverIsTransfer = false;
            transferBurnCompleted = false;
            pendingPolarBody = null;
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
            gravityTurnPolar = false;
            yawInput = 0f;
            rollInput = 0f;
            translationX = 0f;
            translationY = 0f;
            translationZ = 0f;
            targetVessel = null;
            ownDockingNode = null;
            targetDockingNode = null;
            dockingRequested = false;
            automaticDocking = false;
            landingCaptureComplete = false;
            landingDeorbitComplete = false;
            sameVesselDock = false;
            missionAssist = false;
            gravityCommandAlreadyAdvanced = false;
            launchPlanePending = false;
            IsControllingThrottle = false;
            activeManeuverNode = null;
            maneuverBurnStartUT = 0d;
            maneuverBurnDuration = 0f;
            maneuverBurnElapsed = 0f;
            maneuverCommandAlreadyAdvanced = false;
            pendingTransferBody = null;
            pendingTransferNode = null;
            activeManeuverIsTransfer = false;
            transferBurnCompleted = false;
            pendingPolarBody = null;
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
            if (HasModule("CLEARSKIES") && Time.time - lastClearSkiesTime >= 2f)
            {
                CleanupDebris();
                lastClearSkiesTime = Time.time;
            }
            if (HasModule("SAFEABORT") && ShouldSafeAbort())
            {
                ActivateAbort("SAFEABORT: active-stage propulsion is depleted.");
                return;
            }

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
            else if (state == RuntimeState.ProcessingScience)
            {
                TickScience(deltaTime);
                return;
            }
            else if (state == RuntimeState.Rendezvousing)
            {
                TickRendezvous();
                return;
            }
            else if (state == RuntimeState.Docking)
            {
                TickDocking();
                return;
            }
            else if (state == RuntimeState.Landing)
            {
                TickLanding();
                return;
            }
            else if (state == RuntimeState.WaitingForRecovery)
            {
                if (vessel.LandedOrSplashed)
                {
                    state = RuntimeState.Idle;
                    commandIndex++;
                    Status = "Recovery conditions met.";
                    AddLog("RECOVER: vessel is ready for recovery.");
                }
                return;
            }
            else if (state == RuntimeState.WaitingForBody)
            {
                TickWaitingForBody();
                return;
            }
            else if (state == RuntimeState.WaitingForManeuver)
            {
                TickManeuverWait();
                return;
            }
            else if (state == RuntimeState.ExecutingManeuver)
            {
                TickManeuverBurn(deltaTime);
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

                case CommandKind.LaunchToDock:
                    if (!SelectTargetVessel(command.TargetName))
                    {
                        Stop("Launch-to-dock target not found.");
                        return false;
                    }
                    throttle = 1f;
                    SetAutopilot(VesselAutopilot.AutopilotMode.Prograde);
                    Stage();
                    if (vessel.mainBody != null)
                    {
                        gravityTurnProfile = ProfileForBody(vessel.mainBody.bodyName.ToUpperInvariant());
                        gravityTurnElapsed = 0d;
                        gravityTurnPrograde = false;
                        pitchInput = 0f;
                        SetAutopilot(VesselAutopilot.AutopilotMode.StabilityAssist);
                        state = RuntimeState.GravityTurning;
                        gravityCommandAlreadyAdvanced = true;
                        launchPlanePending = targetVessel.orbit != null;
                    }
                    AddLog("LAUNCH TO DOCK TO " + targetVessel.vesselName + ": target selected; ascent gravity turn engaged.");
                    if (targetVessel.orbit != null)
                        AddLog("Target orbit inclination: " + (targetVessel.orbit.inclination * 180d / Math.PI).ToString("0.0") + "deg. Verify launch heading/plane before rendezvous.");
                    commandIndex++;
                    return state == RuntimeState.Idle;

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
                    circularTargetAltitude = command.HasTargetAltitude ? command.Number : -1d;
                    state = RuntimeState.WaitingForApoapsis;
                    Status = "Waiting for apoapsis to circularize.";
                    return true;

                case CommandKind.Distance:
                    dockingDistance = Math.Max(0d, command.Number);
                    AddLog("DISTANCE set to " + dockingDistance.ToString("0.##") + "m.");
                    commandIndex++;
                    return true;

                case CommandKind.GravTurn:
                    gravityTurnProfile = ProfileForBody(command.Body);
                    gravityTurnElapsed = 0d;
                    gravityTurnPrograde = false;
                    gravityTurnPolar = command.Polar;
                    pitchInput = 0f;
                    throttle = 1f;
                    SetAutopilot(VesselAutopilot.AutopilotMode.StabilityAssist);
                    state = RuntimeState.GravityTurning;
                    gravityCommandAlreadyAdvanced = false;
                    launchPlanePending = false;
                    Status = "Gravity turn profile loaded for " + gravityTurnProfile.Body + ".";
                    AddLog("GRAVTURN " + gravityTurnProfile.Body + ": target apoapsis " + gravityTurnProfile.TargetApoapsis.ToString("0") + "m.");
                    return true;

                case CommandKind.PolarOrbit:
                    if (!StartPolarOrbit(command.Body))
                    {
                        Stop("POLAR ORBIT failed: destination body or orbit was unavailable.");
                        return false;
                    }
                    return state == RuntimeState.Idle;

                case CommandKind.Hohmann:
                case CommandKind.Transfer:
                    if (!PlanTransferNode(command.Body))
                    {
                        Stop("Transfer planning failed.");
                        return false;
                    }
                    commandIndex++;
                    return true;

                case CommandKind.Science:
                    if (command.Mode == "COLLECT")
                    {
                        CollectScience();
                        commandIndex++;
                        return true;
                    }
                    if (command.Mode == "TRANSMIT")
                    {
                        TransmitScience();
                        commandIndex++;
                        return true;
                    }
                    StartScienceAuto();
                    return false;

                case CommandKind.DeploySolarPanels:
                    DeploySolarPanels();
                    commandIndex++;
                    return true;

                case CommandKind.ExecuteNode:
                    if (!BeginManeuverExecution())
                    {
                        Stop("EXECUTE NODE failed: no usable maneuver node was found.");
                        return false;
                    }
                    return false;

                case CommandKind.AlignPlane:
                    if (!PlanPlaneAlignmentNode())
                    {
                        Stop("ALIGN PLANE failed: no usable target orbit was found.");
                        return false;
                    }
                    commandIndex++;
                    return true;

                case CommandKind.Rendezvous:
                    if (!SelectTargetVessel(command.TargetName))
                    {
                        Stop("Rendezvous target not found.");
                        return false;
                    }
                    BeginRendezvous(false);
                    return false;

                case CommandKind.DockToTarget:
                    if (!ResolveTargetVessel())
                    {
                        Stop("DOCK TO TARGET failed: select a vessel or docking port first.");
                        return false;
                    }
                    dockingRequested = true;
                    automaticDocking = false;
                    BeginRendezvous(true);
                    return false;

                case CommandKind.AutoDock:
                    if (!ResolveTargetVessel())
                    {
                        Stop("AUTODOCK failed: select a vessel or docking port first.");
                        return false;
                    }
                    dockingRequested = true;
                    automaticDocking = true;
                    BeginRendezvous(true);
                    return false;

                case CommandKind.ApolloDock:
                    if (!BeginApolloDock(command.TargetName))
                    {
                        Stop("Apollo transposition docking failed: no compatible separated vessel or ports were found.");
                        return false;
                    }
                    return false;

                case CommandKind.Land:
                    landingBody = command.Body;
                    landingCaptureComplete = false;
                    landingDeorbitComplete = false;
                    if (vessel == null || vessel.mainBody == null)
                    {
                        Stop("LAND failed: no current celestial body.");
                        return false;
                    }
                    if (!vessel.mainBody.bodyName.Equals(landingBody, StringComparison.OrdinalIgnoreCase))
                    {
                        if (transferBurnCompleted && string.Equals(pendingTransferBody, landingBody, StringComparison.OrdinalIgnoreCase))
                        {
                            state = RuntimeState.WaitingForBody;
                            Status = "Coasting to " + landingBody + " sphere of influence.";
                            AddLog("LAND: transfer burn complete; waiting to enter " + landingBody + " sphere of influence.");
                            return false;
                        }

                        Stop("LAND body mismatch: current body is " + vessel.mainBody.bodyName + ". Execute a transfer to " + landingBody + " first.");
                        return false;
                    }
                    SetAutopilot(VesselAutopilot.AutopilotMode.Retrograde);
                    throttle = 0f;
                    state = RuntimeState.Landing;
                    Status = "Landing on " + landingBody + ".";
                    AddLog("LAND ON " + landingBody + ": descent guidance engaged.");
                    return false;

                case CommandKind.Recover:
                    if (vessel == null || !vessel.LandedOrSplashed)
                    {
                        state = RuntimeState.WaitingForRecovery;
                        Status = "Waiting for landed or splashed recovery state.";
                        AddLog("RECOVER waiting: vessel is not landed or splashed.");
                        return false;
                    }
                    AddLog("RECOVER: vessel is ready for recovery.");
                    commandIndex++;
                    return true;

                case CommandKind.WalkInThePark:
                    missionAssist = true;
                    modules.Add("AUTOSTAGE");
                    modules.Add("SMARTRCS");
                    modules.Add("MAXSCIENCE");
                    modules.Add("AUTOLAND");
                    modules.Add("RENDEZVOUS");
                    modules.Add("DOCKING");
                    modules.Add("RECOVERY");
                    AddLog("WALK IN THE PARK: high-level mission assists enabled for this script.");
                    commandIndex++;
                    return true;

                case CommandKind.Abort:
                    ActivateAbort("ABORT activated.");
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

            bool targetReached = circularTargetAltitude > 0d
                ? periapsis >= circularTargetAltitude * 0.995
                : periapsis >= apoapsis * 0.985;
            if (targetReached)
            {
                throttle = 0f;
                state = RuntimeState.Idle;
                commandIndex++;
                Status = "Circular orbit reached.";
                AddLog(circularTargetAltitude > 0d ? "CIRCULARISE complete at target altitude." : "CIRC complete.");
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

        private void TickRendezvous()
        {
            if (vessel == null || targetVessel == null || targetVessel == vessel)
            {
                Stop("Rendezvous target is no longer available.");
                return;
            }

            Vector3 delta = targetVessel.GetTransform().position - vessel.GetTransform().position;
            float distance = delta.magnitude;
            if (dockingRequested && distance <= Mathf.Max(250f, (float)dockingDistance * 5f))
            {
                if (BeginDocking(false))
                    return;
                Stop("Docking failed: no compatible docking ports were found or the selected target port is unavailable.");
                return;
            }

            if (!dockingRequested && distance <= (float)dockingDistance)
            {
                translationX = translationY = translationZ = 0f;
                SetRcs("OFF");
                state = RuntimeState.Idle;
                commandIndex++;
                Status = "Rendezvous distance reached.";
                AddLog("RENDEZVOUS complete at " + distance.ToString("0.0") + "m.");
                return;
            }

            Vector3 local = vessel.GetTransform().InverseTransformPoint(targetVessel.GetTransform().position);
            Vector3 relativeVelocity = targetVessel.GetSrfVelocity() - vessel.GetSrfVelocity();
            Vector3 localRelativeVelocity = vessel.GetTransform().InverseTransformDirection(relativeVelocity);
            translationX = Mathf.Clamp(local.x * 0.02f + localRelativeVelocity.x * 0.12f, -1f, 1f);
            translationY = Mathf.Clamp(local.y * 0.02f + localRelativeVelocity.y * 0.12f, -1f, 1f);
            translationZ = Mathf.Clamp(local.z * 0.01f + localRelativeVelocity.z * 0.08f, -1f, 1f);
            SetRcs("ON");
            Status = "Rendezvous: " + distance.ToString("0.0") + "m from target.";
        }

        private void TickDocking()
        {
            if (ownDockingNode == null || targetDockingNode == null)
            {
                Stop("Docking ports are no longer available.");
                return;
            }

            Vector3 delta = targetDockingNode.GetTransform().position - ownDockingNode.GetTransform().position;
            float distance = delta.magnitude;
            Transform ownTransform = ownDockingNode.GetTransform();
            Vector3 local = ownTransform.InverseTransformPoint(targetDockingNode.GetTransform().position);
            Vector3 alignment = Vector3.Cross(ownDockingNode.GetFwdVector(), -targetDockingNode.GetFwdVector());
            Vector3 localAlignment = ownTransform.InverseTransformDirection(alignment);
            Vector3 relativeVelocity = targetDockingNode.GetSrfVelocity() - ownDockingNode.GetSrfVelocity();
            Vector3 localRelativeVelocity = ownTransform.InverseTransformDirection(relativeVelocity);

            translationX = Mathf.Clamp(local.x * 0.08f + localRelativeVelocity.x * 0.18f, -1f, 1f);
            translationY = Mathf.Clamp(local.y * 0.08f + localRelativeVelocity.y * 0.18f, -1f, 1f);
            translationZ = Mathf.Clamp(local.z * 0.04f + localRelativeVelocity.z * 0.12f, -1f, 1f);
            pitchInput = Mathf.Clamp(-localAlignment.x * 1.5f, -1f, 1f);
            yawInput = Mathf.Clamp(localAlignment.y * 1.5f, -1f, 1f);
            rollInput = Mathf.Clamp(localAlignment.z * 1.5f, -1f, 1f);
            throttle = 0f;
            SetRcs("ON");

            float relativeSpeed = (ownDockingNode.GetSrfVelocity() - targetDockingNode.GetSrfVelocity()).magnitude;
            if (distance <= 1.25f && relativeSpeed <= 0.75f && Vector3.Dot(ownDockingNode.GetFwdVector(), -targetDockingNode.GetFwdVector()) > 0.95f)
            {
                if (sameVesselDock)
                    ownDockingNode.DockToSameVessel(targetDockingNode);
                else
                    ownDockingNode.DockToVessel(targetDockingNode);

                translationX = translationY = translationZ = 0f;
                pitchInput = yawInput = rollInput = 0f;
                SetRcs("OFF");
                state = RuntimeState.Idle;
                commandIndex++;
                Status = "Docking command sent.";
                AddLog("DOCK complete: ports captured.");
                return;
            }

            Status = "Docking: " + distance.ToString("0.00") + "m, relative speed " + relativeSpeed.ToString("0.00") + "m/s.";
        }

        private void TickWaitingForBody()
        {
            if (vessel == null || vessel.mainBody == null)
            {
                Stop("Transfer arrival failed: vessel no longer has a celestial-body reference.");
                return;
            }

            string waitingBody = !string.IsNullOrEmpty(pendingPolarBody) ? pendingPolarBody : landingBody;
            if (string.IsNullOrEmpty(waitingBody))
            {
                Stop("Transfer arrival failed: no destination body was recorded.");
                return;
            }

            if (!vessel.mainBody.bodyName.Equals(waitingBody, StringComparison.OrdinalIgnoreCase))
            {
                Status = "Coasting to " + waitingBody + "; current sphere of influence is " + vessel.mainBody.bodyName + ".";
                return;
            }

            if (!string.IsNullOrEmpty(pendingPolarBody))
            {
                pendingPolarBody = null;
                pendingTransferBody = null;
                transferBurnCompleted = false;
                if (!StartPolarOrbit(waitingBody))
                {
                    Stop("POLAR ORBIT failed after arrival: no usable orbit was available.");
                    return;
                }
                return;
            }

            pendingTransferBody = null;
            transferBurnCompleted = false;
            landingCaptureComplete = false;
            landingDeorbitComplete = false;
            SetAutopilot(VesselAutopilot.AutopilotMode.Retrograde);
            throttle = 0f;
            state = RuntimeState.Landing;
            Status = "Arrived at " + landingBody + "; landing guidance engaged.";
            AddLog("TRANSFER arrival: entered " + landingBody + " sphere of influence.");
        }

        private bool StartPolarOrbit(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName))
                bodyName = vessel == null || vessel.mainBody == null ? string.Empty : vessel.mainBody.bodyName;
            if (vessel == null || vessel.mainBody == null || string.IsNullOrEmpty(bodyName) ||
                !vessel.mainBody.bodyName.Equals(bodyName, StringComparison.OrdinalIgnoreCase))
            {
                if (transferBurnCompleted && string.Equals(pendingTransferBody, bodyName, StringComparison.OrdinalIgnoreCase))
                {
                    pendingPolarBody = bodyName;
                    state = RuntimeState.WaitingForBody;
                    Status = "Coasting to " + bodyName + " for polar-orbit insertion.";
                    AddLog("POLAR ORBIT: transfer burn complete; waiting to enter " + bodyName + " sphere of influence.");
                    return true;
                }
                return false;
            }

            if (vessel.orbit == null || vessel.patchedConicSolver == null)
                return false;

            int nodeCountBefore = vessel.patchedConicSolver.maneuverNodes == null
                ? 0
                : vessel.patchedConicSolver.maneuverNodes.Count;
            if (!PlanBodyPlaneNode(true))
                return false;
            int nodeCountAfter = vessel.patchedConicSolver.maneuverNodes == null
                ? 0
                : vessel.patchedConicSolver.maneuverNodes.Count;

            if (nodeCountAfter > nodeCountBefore && missionAssist)
            {
                maneuverCommandAlreadyAdvanced = true;
                if (!BeginManeuverExecution())
                {
                    maneuverCommandAlreadyAdvanced = false;
                    return false;
                }
                return true;
            }

            commandIndex++;
            state = RuntimeState.Idle;
            Status = "Polar-orbit plane node created.";
            return true;
        }

        private void TickLanding()
        {
            if (vessel == null || vessel.mainBody == null || !vessel.mainBody.bodyName.Equals(landingBody, StringComparison.OrdinalIgnoreCase))
            {
                Stop("Landing body is no longer current.");
                return;
            }
            if (vessel.LandedOrSplashed)
            {
                throttle = 0f;
                pitchInput = yawInput = rollInput = 0f;
                SetRcs("OFF");
                state = RuntimeState.Idle;
                commandIndex++;
                Status = "Landing complete.";
                AddLog("LAND complete.");
                return;
            }

            SetAutopilot(VesselAutopilot.AutopilotMode.Retrograde);
            if (!landingCaptureComplete && vessel.orbit != null)
            {
                bool hyperbolicArrival = vessel.orbit.eccentricity >= 1d;
                if (hyperbolicArrival && vessel.altitude > 5000d)
                {
                    throttle = 0.8f;
                    Status = "Landing: performing retrograde capture burn.";
                    return;
                }

                landingCaptureComplete = true;
                if (hyperbolicArrival)
                    AddLog("LAND: hyperbolic arrival captured into an elliptical orbit.");
            }

            if (!landingDeorbitComplete && vessel.orbit != null && vessel.orbit.PeA > 0d && vessel.altitude > 5000d)
            {
                if (vessel.orbit.timeToPe > 1d)
                {
                    throttle = 0f;
                    Status = "Landing: waiting for deorbit burn at periapsis.";
                    return;
                }

                throttle = 0.65f;
                Status = "Landing: performing retrograde deorbit burn.";
                if (vessel.orbit.PeA <= 0d)
                {
                    throttle = 0f;
                    landingDeorbitComplete = true;
                    AddLog("LAND: deorbit burn complete; descending to powered landing.");
                }
                return;
            }

            landingDeorbitComplete = true;
            double radarAltitude = Math.Max(0d, vessel.radarAltitude);
            double descentSpeed = Math.Max(0d, -vessel.verticalSpeed);
            if (radarAltitude > 2000d)
                throttle = descentSpeed > 80d ? 0.45f : 0f;
            else
                throttle = Mathf.Clamp((float)((descentSpeed - 2d) * 0.04d), 0f, 1f);
            Status = "Landing: " + radarAltitude.ToString("0") + "m, descent " + descentSpeed.ToString("0.0") + "m/s.";
        }

        private void BeginRendezvous(bool forDocking)
        {
            dockingRequested = forDocking;
            SetRcs("ON");
            state = RuntimeState.Rendezvousing;
            Status = "Rendezvous guidance active.";
            AddLog(forDocking ? "Rendezvous started before docking." : "Rendezvous started.");
        }

        private bool BeginDocking(bool sameVessel)
        {
            if (vessel == null || targetVessel == null)
                return false;

            List<ModuleDockingNode> ownPorts = vessel.FindPartModulesImplementing<ModuleDockingNode>();
            List<ModuleDockingNode> targetPorts = targetVessel.FindPartModulesImplementing<ModuleDockingNode>();
            float bestDistance = float.MaxValue;
            ModuleDockingNode bestOwn = null;
            ModuleDockingNode bestTarget = null;
            ModuleDockingNode manuallySelectedTarget = null;
            if (!sameVessel && !automaticDocking && FlightGlobals.activeTarget != null)
            {
                List<ModuleDockingNode> selectedPorts = FlightGlobals.activeTarget.FindModulesImplementing<ModuleDockingNode>();
                if (selectedPorts.Count > 0)
                    manuallySelectedTarget = selectedPorts[0];
            }
            if (!sameVessel && !automaticDocking && manuallySelectedTarget == null)
                return false;
            for (int i = 0; i < ownPorts.Count; i++)
            {
                for (int j = 0; j < targetPorts.Count; j++)
                {
                    if (ownPorts[i] == targetPorts[j] ||
                        (manuallySelectedTarget != null && targetPorts[j] != manuallySelectedTarget) ||
                        (!string.IsNullOrEmpty(ownPorts[i].nodeType) && !string.IsNullOrEmpty(targetPorts[j].nodeType) &&
                         !ownPorts[i].nodeType.Equals(targetPorts[j].nodeType, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    float candidateDistance = Vector3.Distance(ownPorts[i].GetTransform().position, targetPorts[j].GetTransform().position);
                    if (candidateDistance < bestDistance)
                    {
                        bestDistance = candidateDistance;
                        bestOwn = ownPorts[i];
                        bestTarget = targetPorts[j];
                    }
                }
            }

            if (bestOwn == null || bestTarget == null)
                return false;

            ownDockingNode = bestOwn;
            targetDockingNode = bestTarget;
            sameVesselDock = sameVessel;
            ownDockingNode.MakeReferenceTransform();
            targetDockingNode.SetAsTarget();
            SetRcs("ON");
            state = RuntimeState.Docking;
            Status = "Docking autopilot active.";
            AddLog((automaticDocking ? "Docking ports selected automatically" : "Selected docking port acquired") + "; approach distance " + bestDistance.ToString("0.0") + "m.");
            return true;
        }

        private bool BeginSameVesselDock()
        {
            targetVessel = vessel;
            return BeginDocking(true);
        }

        private bool BeginApolloDock(string targetName)
        {
            automaticDocking = true;
            if (!string.IsNullOrEmpty(targetName) && !SelectTargetVessel(targetName))
                return false;

            if (targetVessel == null || targetVessel == vessel)
            {
                if (!FindNearestDockableVessel())
                    return BeginSameVesselDock();
            }

            dockingRequested = true;
            AddLog("APOLLO DOCK: transposition rotation and RCS approach guidance enabled.");
            BeginRendezvous(true);
            return true;
        }

        private bool FindNearestDockableVessel()
        {
            if (vessel == null || FlightGlobals.Vessels == null || vessel.GetTransform() == null)
                return false;

            float nearestDistance = float.MaxValue;
            Vessel nearest = null;
            for (int i = 0; i < FlightGlobals.Vessels.Count; i++)
            {
                Vessel candidate = FlightGlobals.Vessels[i];
                if (candidate == null || candidate == vessel || candidate.GetTransform() == null)
                    continue;
                if (candidate.FindPartModulesImplementing<ModuleDockingNode>().Count == 0)
                    continue;
                float distance = Vector3.Distance(vessel.GetTransform().position, candidate.GetTransform().position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = candidate;
                }
            }

            targetVessel = nearest;
            if (targetVessel != null && FlightGlobals.fetch != null)
                FlightGlobals.fetch.SetVesselTarget(targetVessel, true);
            return targetVessel != null;
        }

        private bool ResolveTargetVessel()
        {
            if (targetVessel != null && targetVessel != vessel)
                return true;

            if (FlightGlobals.activeTarget != null && FlightGlobals.activeTarget.vessel != null && FlightGlobals.activeTarget.vessel != vessel)
            {
                targetVessel = FlightGlobals.activeTarget.vessel;
                return true;
            }

            if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget is Vessel)
            {
                targetVessel = (Vessel)FlightGlobals.fetch.VesselTarget;
                return targetVessel != vessel;
            }
            return false;
        }

        private bool SelectTargetVessel(string name)
        {
            if (name.Equals("TARGET", StringComparison.OrdinalIgnoreCase))
                return ResolveTargetVessel();
            if (FlightGlobals.Vessels == null)
                return false;

            for (int i = 0; i < FlightGlobals.Vessels.Count; i++)
            {
                Vessel candidate = FlightGlobals.Vessels[i];
                if (candidate != null && candidate != vessel &&
                    (candidate.vesselName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                     candidate.vesselName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    targetVessel = candidate;
                    if (FlightGlobals.fetch != null)
                        FlightGlobals.fetch.SetVesselTarget(candidate, true);
                    return true;
                }
            }
            return false;
        }

        private bool BeginManeuverExecution()
        {
            if (vessel == null || vessel.patchedConicSolver == null || vessel.orbit == null ||
                vessel.patchedConicSolver.maneuverNodes == null)
                return false;

            activeManeuverNode = null;
            activeManeuverIsTransfer = false;
            double now = Planetarium.GetUniversalTime();
            double bestUT = double.MaxValue;
            for (int i = 0; i < vessel.patchedConicSolver.maneuverNodes.Count; i++)
            {
                ManeuverNode candidate = vessel.patchedConicSolver.maneuverNodes[i];
                if (candidate != null && candidate.UT >= now - 2d && candidate.UT < bestUT)
                {
                    activeManeuverNode = candidate;
                    bestUT = candidate.UT;
                }
            }

            if (activeManeuverNode == null)
                return false;

            double deltaV = activeManeuverNode.GetBurnVector(vessel.orbit).magnitude;
            if (double.IsNaN(deltaV) || deltaV <= 0d)
            {
                activeManeuverNode = null;
                return false;
            }

            double acceleration = 2d;
            if (vessel.VesselDeltaV != null && vessel.VesselDeltaV.TotalBurnTime > 0d && vessel.VesselDeltaV.TotalDeltaVActual > 0d)
                acceleration = vessel.VesselDeltaV.TotalDeltaVActual / vessel.VesselDeltaV.TotalBurnTime;

            maneuverBurnDuration = Mathf.Clamp((float)(deltaV / Math.Max(0.1d, acceleration)), 1f, 300f);
            maneuverBurnStartUT = activeManeuverNode.UT - maneuverBurnDuration * 0.5d;
            maneuverBurnElapsed = 0f;
            activeManeuverIsTransfer = activeManeuverNode == pendingTransferNode;
            throttle = 0f;
            state = RuntimeState.WaitingForManeuver;
            Status = "Waiting for maneuver node burn.";
            AddLog("EXECUTE NODE: " + deltaV.ToString("0.0") + "m/s burn, estimated duration " + maneuverBurnDuration.ToString("0.0") + "s.");

            if (HasModule("AUTOWARP") && TimeWarp.fetch != null && maneuverBurnStartUT - now > 30d)
                TimeWarp.fetch.WarpTo(Math.Max(now + 1d, maneuverBurnStartUT - 30d));
            return true;
        }

        private bool PlanPlaneAlignmentNode()
        {
            if (!ResolveTargetVessel() || vessel == null || vessel.orbit == null || targetVessel.orbit == null ||
                vessel.patchedConicSolver == null || vessel.orbit.referenceBody != targetVessel.orbit.referenceBody)
            {
                AddLog("ALIGN PLANE failed: target must be a vessel in the same sphere of influence.");
                return false;
            }

            double relativeInclination = vessel.orbit.GetRelativeInclination(targetVessel.orbit);
            if (double.IsNaN(relativeInclination) || relativeInclination < 0.001d)
            {
                AddLog("ALIGN PLANE: current and target orbital planes are already aligned.");
                return true;
            }

            double now = Planetarium.GetUniversalTime();
            double nodeTrueAnomaly = Orbit.AscendingNodeTrueAnomaly(vessel.orbit, targetVessel.orbit);
            double nodeUT = vessel.orbit.GetUTforTrueAnomaly(nodeTrueAnomaly, vessel.orbit.period);
            if (double.IsNaN(nodeUT) || double.IsInfinity(nodeUT) || nodeUT < now + 5d)
                nodeUT = vessel.orbit.GetUTforTrueAnomaly(nodeTrueAnomaly, vessel.orbit.period * 2d);
            if (double.IsNaN(nodeUT) || double.IsInfinity(nodeUT) || nodeUT < now + 5d)
                nodeUT = now + Math.Max(5d, vessel.orbit.GetTimeToPeriapsis());

            Vector3d sourcePosition = vessel.orbit.getRelativePositionAtUT(nodeUT).normalized;
            Vector3d sourceNormal = vessel.orbit.GetOrbitNormal().normalized;
            Vector3d targetNormal = targetVessel.orbit.GetOrbitNormal().normalized;
            Vector3d sourcePrograde = Vector3d.Cross(sourceNormal, sourcePosition).normalized;
            Vector3d targetPrograde = Vector3d.Cross(targetNormal, sourcePosition).normalized;
            double orbitalSpeed = vessel.orbit.getOrbitalVelocityAtUT(nodeUT).magnitude;
            double normalDeltaV = Vector3d.Dot(targetPrograde - sourcePrograde, sourceNormal) * orbitalSpeed;
            if (double.IsNaN(normalDeltaV) || Math.Abs(normalDeltaV) < 0.1d)
            {
                AddLog("ALIGN PLANE: calculated correction is below 0.1m/s.");
                return true;
            }

            ManeuverNode node = vessel.patchedConicSolver.AddManeuverNode(nodeUT);
            node.DeltaV = new Vector3d(0d, normalDeltaV, 0d);
            Status = "Plane-alignment node created.";
            AddLog("ALIGN PLANE: " + (relativeInclination * 180d / Math.PI).ToString("0.0") + "deg correction, estimated dV " + Math.Abs(normalDeltaV).ToString("0.0") + "m/s.");
            return true;
        }

        private bool PlanBodyPlaneNode(bool polar)
        {
            if (vessel == null || vessel.mainBody == null || vessel.orbit == null ||
                vessel.patchedConicSolver == null || vessel.orbit.period <= 0d)
            {
                AddLog("PLANE: current orbit is not usable for a plane-change node.");
                return false;
            }

            Vector3d equatorialNormal = vessel.mainBody.RotationAxis.normalized;
            Vector3d currentNormal = vessel.orbit.GetOrbitNormal().normalized;
            if (equatorialNormal.sqrMagnitude <= 0d || currentNormal.sqrMagnitude <= 0d)
                return false;

            double planeDot = Math.Abs(Vector3d.Dot(currentNormal, equatorialNormal));
            Vector3d desiredNormal = equatorialNormal;
            Vector3d nodeDirection;
            if (polar)
            {
                nodeDirection = Vector3d.Cross(currentNormal, equatorialNormal).normalized;
                if (nodeDirection.sqrMagnitude <= 0.000001d)
                    nodeDirection = vessel.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime()).normalized;
                desiredNormal = Vector3d.Cross(equatorialNormal, nodeDirection).normalized;
            }
            else
            {
                if (planeDot > 0.9995d)
                {
                    AddLog("EQUATORIAL ORBIT: current orbital plane is already equatorial.");
                    return true;
                }
                nodeDirection = Vector3d.Cross(currentNormal, desiredNormal).normalized;
            }

            if (desiredNormal.sqrMagnitude <= 0.000001d || nodeDirection.sqrMagnitude <= 0.000001d)
                return false;

            double now = Planetarium.GetUniversalTime();
            double nodeTrueAnomaly = vessel.orbit.TrueAnomalyFromVector(nodeDirection);
            double nodeUT = vessel.orbit.GetUTforTrueAnomaly(nodeTrueAnomaly, vessel.orbit.period);
            if (double.IsNaN(nodeUT) || double.IsInfinity(nodeUT) || nodeUT < now + 5d)
                nodeUT = vessel.orbit.GetUTforTrueAnomaly(nodeTrueAnomaly, vessel.orbit.period * 2d);
            if (double.IsNaN(nodeUT) || double.IsInfinity(nodeUT) || nodeUT < now + 5d)
                nodeUT = now + Math.Max(5d, vessel.orbit.GetTimeToPeriapsis());

            Vector3d position = vessel.orbit.getRelativePositionAtUT(nodeUT).normalized;
            Vector3d currentVelocity = vessel.orbit.getOrbitalVelocityAtUT(nodeUT);
            if (position.sqrMagnitude <= 0d || currentVelocity.sqrMagnitude <= 0d)
                return false;

            Vector3d desiredPrograde = Vector3d.Cross(desiredNormal, position).normalized;
            if (Vector3d.Dot(desiredPrograde, currentVelocity) < 0d)
                desiredPrograde = -desiredPrograde;
            double normalDeltaV = Vector3d.Dot(desiredPrograde * currentVelocity.magnitude - currentVelocity, currentNormal);
            if (double.IsNaN(normalDeltaV) || Math.Abs(normalDeltaV) < 0.1d)
            {
                AddLog(polar ? "POLAR ORBIT: current orbital plane is already close to polar." : "EQUATORIAL ORBIT: correction is below 0.1m/s.");
                return true;
            }

            ManeuverNode node = vessel.patchedConicSolver.AddManeuverNode(nodeUT);
            node.DeltaV = new Vector3d(0d, normalDeltaV, 0d);
            AddLog((polar ? "POLAR ORBIT" : "EQUATORIAL ORBIT") + ": plane node created; estimated dV " + Math.Abs(normalDeltaV).ToString("0.0") + "m/s.");
            return true;
        }

        private void TickManeuverWait()
        {
            if (activeManeuverNode == null)
            {
                Stop("Maneuver node disappeared before execution.");
                return;
            }

            if (Planetarium.GetUniversalTime() < maneuverBurnStartUT)
                return;

            if (TimeWarp.fetch != null)
                TimeWarp.SetRate(0, true);
            SetAutopilot(VesselAutopilot.AutopilotMode.Maneuver);
            throttle = 1f;
            maneuverBurnElapsed = 0f;
            state = RuntimeState.ExecutingManeuver;
            Status = "Executing maneuver burn.";
            AddLog("EXECUTE NODE: burn started.");
        }

        private void TickManeuverBurn(float deltaTime)
        {
            if (activeManeuverNode == null)
            {
                Stop("Maneuver node disappeared during execution.");
                return;
            }

            throttle = 1f;
            maneuverBurnElapsed += Mathf.Max(0f, deltaTime);
            if (maneuverBurnElapsed < maneuverBurnDuration)
                return;

            throttle = 0f;
            activeManeuverNode.RemoveSelf();
            activeManeuverNode = null;
            if (activeManeuverIsTransfer)
            {
                transferBurnCompleted = true;
                pendingTransferNode = null;
                activeManeuverIsTransfer = false;
                AddLog("TRANSFER: burn complete; coasting to target sphere of influence.");
            }
            SetAutopilot(VesselAutopilot.AutopilotMode.Prograde);
            state = RuntimeState.Idle;
            if (!maneuverCommandAlreadyAdvanced)
                commandIndex++;
            maneuverCommandAlreadyAdvanced = false;
            Status = "Maneuver burn complete.";
            AddLog("EXECUTE NODE: burn complete; maneuver node removed.");
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
                gravityTurnProfile = null;
                if (launchPlanePending && vessel.patchedConicSolver != null)
                {
                    launchPlanePending = false;
                    int nodeCountBefore = vessel.patchedConicSolver.maneuverNodes == null
                        ? 0
                        : vessel.patchedConicSolver.maneuverNodes.Count;
                    PlanPlaneAlignmentNode();
                    int nodeCountAfter = vessel.patchedConicSolver.maneuverNodes == null
                        ? 0
                        : vessel.patchedConicSolver.maneuverNodes.Count;
                    if (nodeCountAfter > nodeCountBefore)
                    {
                        maneuverCommandAlreadyAdvanced = true;
                        if (BeginManeuverExecution())
                        {
                            gravityCommandAlreadyAdvanced = false;
                            Status = "Gravity turn complete; executing target-plane correction.";
                            AddLog("LAUNCH TO DOCK: target-plane correction burn queued.");
                            return;
                        }
                        maneuverCommandAlreadyAdvanced = false;
                    }
                }
                else if (vessel.patchedConicSolver != null)
                {
                    int nodeCountBefore = vessel.patchedConicSolver.maneuverNodes == null
                        ? 0
                        : vessel.patchedConicSolver.maneuverNodes.Count;
                    PlanBodyPlaneNode(gravityTurnPolar);
                    int nodeCountAfter = vessel.patchedConicSolver.maneuverNodes == null
                        ? 0
                        : vessel.patchedConicSolver.maneuverNodes.Count;
                    if (nodeCountAfter > nodeCountBefore)
                    {
                        // GRAVTURN has not advanced its command yet, so the plane-burn
                        // completion is allowed to advance to the following command.
                        maneuverCommandAlreadyAdvanced = false;
                        if (BeginManeuverExecution())
                        {
                            Status = gravityTurnPolar
                                ? "Gravity turn complete; executing polar-plane correction."
                                : "Gravity turn complete; executing equatorial-plane correction.";
                            AddLog("GRAVTURN: " + (gravityTurnPolar ? "polar" : "equatorial") + " plane correction burn queued.");
                            return;
                        }
                    }
                }

                state = RuntimeState.Idle;
                if (!gravityCommandAlreadyAdvanced)
                    commandIndex++;
                gravityCommandAlreadyAdvanced = false;
                Status = "Gravity turn complete.";
                AddLog("GRAVTURN complete.");
                return;
            }

            if (apoapsis > gravityTurnProfile.TargetApoapsis * 0.9)
                throttle = 0.35f;
            else
                throttle = 1f;
        }

        private bool PlanTransferNode(string bodyName)
        {
            if (vessel == null || vessel.orbit == null || vessel.patchedConicSolver == null)
            {
                AddLog("TRANSFER failed: vessel has no usable orbit or maneuver planner.");
                return false;
            }

            CelestialBody target = FindBody(bodyName);
            CelestialBody currentBody = vessel.mainBody;
            if (target == null || currentBody == null || target == currentBody || target.orbit == null)
            {
                AddLog("TRANSFER failed: celestial body '" + bodyName + "' was not found or is not a transfer target.");
                return false;
            }

            CelestialBody orbitBody = vessel.orbit.referenceBody;
            double departureRadius = vessel.orbit.PeR;
            if (departureRadius <= 0 || vessel.orbit.semiMajorAxis <= 0)
            {
                AddLog("TRANSFER failed: current orbit is not stable enough for a transfer node.");
                return false;
            }

            double deltaV;
            Orbit transferSourceOrbit;
            bool localTransfer = target.orbit.referenceBody == orbitBody;
            if (localTransfer)
            {
                double targetRadius = target.orbit.semiMajorAxis;
                deltaV = HohmannDeltaV(orbitBody.gravParameter, departureRadius, vessel.orbit.semiMajorAxis, targetRadius);
                transferSourceOrbit = vessel.orbit;
            }
            else
            {
                if (currentBody.orbit == null || target.orbit.referenceBody != currentBody.orbit.referenceBody)
                {
                    AddLog("TRANSFER failed: source and target are not in the same parent sphere of influence.");
                    return false;
                }

                double parentMu = target.orbit.referenceBody.gravParameter;
                double sourceRadius = currentBody.orbit.semiMajorAxis;
                double targetRadius = target.orbit.semiMajorAxis;
                double transferSemiMajorAxis = (sourceRadius + targetRadius) * 0.5;
                double sourceCircularVelocity = Math.Sqrt(parentMu / sourceRadius);
                double transferVelocity = Math.Sqrt(parentMu * (2d / sourceRadius - 1d / transferSemiMajorAxis));
                double excessVelocity = transferVelocity - sourceCircularVelocity;
                double desiredPeriapsisVelocity = Math.Sqrt(excessVelocity * excessVelocity + 2d * currentBody.gravParameter / departureRadius);
                double currentPeriapsisVelocity = Math.Sqrt(orbitBody.gravParameter * (2d / departureRadius - 1d / vessel.orbit.semiMajorAxis));
                deltaV = desiredPeriapsisVelocity - currentPeriapsisVelocity;
                transferSourceOrbit = currentBody.orbit;
            }

            if (transferSourceOrbit == null || transferSourceOrbit.period <= 0d || target.orbit.period <= 0d)
            {
                AddLog("TRANSFER failed: source or target orbit has no usable period.");
                return false;
            }

            double hohmannSemiMajorAxis = (transferSourceOrbit.semiMajorAxis + target.orbit.semiMajorAxis) * 0.5d;
            double transferTime = Math.PI * Math.Sqrt(Math.Pow(hohmannSemiMajorAxis, 3d) / target.orbit.referenceBody.gravParameter);
            bool targetIsOutside = target.orbit.semiMajorAxis > transferSourceOrbit.semiMajorAxis;
            double targetMeanMotion = 2d * Math.PI / target.orbit.period;
            double desiredPhase = targetIsOutside
                ? Math.PI - targetMeanMotion * transferTime
                : -(Math.PI - targetMeanMotion * transferTime);
            double now = Planetarium.GetUniversalTime();
            double synodicPeriod = 2d * Math.PI / Math.Abs((2d * Math.PI / transferSourceOrbit.period) - targetMeanMotion);
            if (double.IsNaN(synodicPeriod) || double.IsInfinity(synodicPeriod) || synodicPeriod < 60d)
                synodicPeriod = Math.Max(transferSourceOrbit.period, target.orbit.period);

            double phaseUT = FindBestTransferWindow(transferSourceOrbit, target.orbit, vessel.orbit, now, desiredPhase, synodicPeriod);
            double nodeUT = Math.Max(now + 5d, phaseUT);
            ManeuverNode node = vessel.patchedConicSolver.AddManeuverNode(nodeUT);
            node.DeltaV = new Vector3d(deltaV, 0d, 0d);
            pendingTransferBody = target.bodyName;
            pendingTransferNode = node;
            transferBurnCompleted = false;
            double currentPhase = SignedPhaseAngle(transferSourceOrbit.getRelativePositionAtUT(now), target.orbit.getRelativePositionAtUT(now), transferSourceOrbit.GetOrbitNormal());
            double phaseError = AngleDifference(currentPhase, desiredPhase) * 180d / Math.PI;
            Status = "Transfer window node created for " + target.bodyName + ".";
            AddLog("TRANSFER to " + target.bodyName + ": window in " + Math.Max(0d, nodeUT - now).ToString("0") + "s, estimated dV " + Math.Abs(deltaV).ToString("0.0") + "m/s, phase error " + phaseError.ToString("0.0") + "deg.");
            if (HasModule("AUTOWARP") && TimeWarp.fetch != null && nodeUT - now > 30d)
            {
                TimeWarp.fetch.WarpTo(Math.Max(now + 1d, nodeUT - 30d));
                AddLog("AUTOWARP: advancing to 30 seconds before the transfer window.");
            }
            if (Math.Abs(transferSourceOrbit.GetRelativeInclination(target.orbit)) > 0.01d)
                AddLog("TRANSFER warning: source/target relative inclination is " + (transferSourceOrbit.GetRelativeInclination(target.orbit) * 180d / Math.PI).ToString("0.0") + "deg; plane alignment is not included in this node.");
            if (missionAssist)
            {
                maneuverCommandAlreadyAdvanced = true;
                if (!BeginManeuverExecution())
                {
                    maneuverCommandAlreadyAdvanced = false;
                    AddLog("WALK IN THE PARK: transfer node was created but could not be auto-executed.");
                }
            }
            return true;
        }

        private static double FindBestTransferWindow(Orbit phaseSourceOrbit, Orbit targetOrbit, Orbit nodeOrbit, double now, double desiredPhase, double horizon)
        {
            double bestUT = now;
            double bestError = double.MaxValue;
            double firstPeriapsis = nodeOrbit.GetNextPeriapsisTime(now);
            if (double.IsNaN(firstPeriapsis) || double.IsInfinity(firstPeriapsis) || firstPeriapsis < now)
                firstPeriapsis = now;
            double nodePeriod = nodeOrbit.period > 0d ? nodeOrbit.period : horizon / 128d;
            int totalCandidates = Math.Max(1, (int)Math.Ceiling(horizon / Math.Max(1d, nodePeriod)));
            const int maxSamples = 2048;
            int stride = Math.Max(1, (int)Math.Ceiling((double)totalCandidates / maxSamples));
            int samples = Math.Max(1, (int)Math.Ceiling((double)totalCandidates / stride));
            for (int i = 0; i <= samples; i++)
            {
                double ut = firstPeriapsis + nodePeriod * stride * i;
                if (ut > now + horizon)
                    break;
                double phase = SignedPhaseAngle(phaseSourceOrbit.getRelativePositionAtUT(ut), targetOrbit.getRelativePositionAtUT(ut), phaseSourceOrbit.GetOrbitNormal());
                double error = Math.Abs(AngleDifference(phase, desiredPhase));
                if (error < bestError)
                {
                    bestError = error;
                    bestUT = ut;
                }
            }
            return bestUT;
        }

        private static double SignedPhaseAngle(Vector3d source, Vector3d target, Vector3d normal)
        {
            if (source.sqrMagnitude <= 0d || target.sqrMagnitude <= 0d)
                return 0d;
            return Math.Atan2(Vector3d.Dot(normal, Vector3d.Cross(source, target)), Vector3d.Dot(source, target));
        }

        private static double AngleDifference(double a, double b)
        {
            double difference = a - b;
            while (difference > Math.PI)
                difference -= 2d * Math.PI;
            while (difference < -Math.PI)
                difference += 2d * Math.PI;
            return difference;
        }

        private void StartScienceAuto()
        {
            if (vessel == null || vessel.parts == null)
            {
                AddLog("SCIENCE failed: no active vessel.");
                Stop("Science unavailable.");
                return;
            }

            int deployed = 0;
            foreach (Part part in vessel.parts)
            {
                List<ModuleScienceExperiment> experiments = part.FindModulesImplementing<ModuleScienceExperiment>();
                for (int i = 0; i < experiments.Count; i++)
                {
                    ModuleScienceExperiment experiment = experiments[i];
                    if (!experiment.HasExperimentData)
                    {
                        experiment.DeployExperiment();
                        deployed++;
                    }
                }
            }

            scienceTimer = deployed > 0 ? 2f : 0f;
            state = RuntimeState.ProcessingScience;
            Status = deployed > 0 ? "Running " + deployed + " science experiment(s)." : "Collecting available science.";
            AddLog("SCIENCE AUTO: started " + deployed + " experiment(s).");
        }

        private void TickScience(float deltaTime)
        {
            scienceTimer -= Mathf.Max(0f, deltaTime);
            if (scienceTimer > 0f)
                return;

            CollectScience();
            if (HasModule("MAXSCIENCE"))
                TransmitScience();
            state = RuntimeState.Idle;
            commandIndex++;
            Status = "Science collection complete.";
            AddLog("SCIENCE AUTO complete.");
        }

        private void CollectScience()
        {
            if (vessel == null || vessel.parts == null)
                return;

            int collected = 0;
            foreach (Part part in vessel.parts)
            {
                List<ModuleScienceExperiment> experiments = part.FindModulesImplementing<ModuleScienceExperiment>();
                for (int i = 0; i < experiments.Count; i++)
                {
                    if (experiments[i].HasExperimentData)
                    {
                        experiments[i].CollectDataExternalEvent();
                        collected++;
                    }
                }
            }
            AddLog("SCIENCE: collected data from " + collected + " experiment(s).");
        }

        private void TransmitScience()
        {
            if (vessel == null || vessel.parts == null)
            {
                AddLog("SCIENCE TRANSMIT failed: no active vessel.");
                return;
            }

            int transmitters = 0;
            foreach (Part part in vessel.parts)
            {
                List<ModuleDataTransmitter> modulesOnPart = part.FindModulesImplementing<ModuleDataTransmitter>();
                for (int i = 0; i < modulesOnPart.Count; i++)
                {
                    ModuleDataTransmitter transmitter = modulesOnPart[i];
                    if (transmitter.CanTransmit())
                    {
                        transmitter.StartTransmission();
                        transmitters++;
                    }
                }
            }
            AddLog("SCIENCE TRANSMIT: started " + transmitters + " transmitter(s).");
        }

        private void DeploySolarPanels()
        {
            if (vessel == null || vessel.parts == null)
            {
                AddLog("DEPLOY SOLAR PANELS failed: no active vessel.");
                return;
            }

            int deployed = 0;
            foreach (Part part in vessel.parts)
            {
                List<ModuleDeployableSolarPanel> panels = part.FindModulesImplementing<ModuleDeployableSolarPanel>();
                for (int i = 0; i < panels.Count; i++)
                {
                    panels[i].Extend();
                    deployed++;
                }
            }
            AddLog("DEPLOY SOLAR PANELS: commanded " + deployed + " panel(s) to extend.");
        }

        private static double HohmannDeltaV(double mu, double departureRadius, double currentSemiMajorAxis, double targetRadius)
        {
            double transferSemiMajorAxis = (departureRadius + targetRadius) * 0.5;
            double currentVelocity = Math.Sqrt(mu * (2d / departureRadius - 1d / currentSemiMajorAxis));
            double transferVelocity = Math.Sqrt(mu * (2d / departureRadius - 1d / transferSemiMajorAxis));
            return transferVelocity - currentVelocity;
        }

        private static CelestialBody FindBody(string bodyName)
        {
            if (FlightGlobals.Bodies == null)
                return null;

            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body != null && string.Equals(body.bodyName, bodyName, StringComparison.OrdinalIgnoreCase))
                    return body;
            }
            return null;
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

        private void ActivateAbort(string message)
        {
            if (vessel != null && vessel.ActionGroups != null)
                vessel.ActionGroups.ToggleGroup(KSPActionGroup.Abort);
            AddLog(message);
            Stop(message);
        }

        private bool ShouldSafeAbort()
        {
            if (vessel == null || vessel.LandedOrSplashed || throttle < 0.2f || vessel.currentStage <= 0)
                return false;

            bool hasLiquidFuel;
            bool hasOxidizer;
            double liquidFuel = ReadStageResource("LiquidFuel", out hasLiquidFuel);
            double oxidizer = ReadStageResource("Oxidizer", out hasOxidizer);
            return (hasLiquidFuel || hasOxidizer) && liquidFuel <= 0.1d && oxidizer <= 0.1d;
        }

        private void CleanupDebris()
        {
            if (vessel == null || FlightGlobals.Vessels == null)
                return;

            for (int i = FlightGlobals.Vessels.Count - 1; i >= 0; i--)
            {
                Vessel debris = FlightGlobals.Vessels[i];
                if (debris == null || debris == vessel || debris.vesselType != VesselType.Debris || debris.GetCrewCount() > 0)
                    continue;
                if (debris.mainBody != vessel.mainBody || (debris.GetWorldPos3D() - vessel.GetWorldPos3D()).sqrMagnitude < 1000000d)
                    continue;

                debris.Die();
                AddLog("CLEARSKIES: removed unmanned debris " + debris.vesselName + ".");
            }
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
            if (condition.IsOrbitState)
            {
                bool safe = IsSafeOrbit();
                result = condition.OrbitSafe ? safe : !safe;
                return condition.Negated ? !result : result;
            }
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

        private bool IsSafeOrbit()
        {
            if (vessel == null || vessel.mainBody == null || vessel.orbit == null || vessel.LandedOrSplashed)
                return false;

            double atmosphereDepth = vessel.mainBody.atmosphere ? vessel.mainBody.atmosphereDepth : 0d;
            double minimumPeriapsis = Math.Max(0d, atmosphereDepth) + 100d;
            return vessel.orbit.ApA > minimumPeriapsis &&
                   vessel.orbit.PeA > minimumPeriapsis &&
                   vessel.orbit.eccentricity < 1d &&
                   vessel.altitude > minimumPeriapsis;
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
