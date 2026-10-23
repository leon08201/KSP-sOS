using System;
using System.Collections.Generic;
using KSPSOS;

internal static class ParserSmoke
{
    private static void Main()
    {
        string valid = "using maxscience;\nSCIENCE AUTO;\nSCIENCE TRANSMIT;\nSET READY = TRUE;\nHOHMANN TO DUNA;\nALIGN PLANE TO TARGET;\nEXECUTE NODE;\nAPOLLO DOCK TO TARGET;\nIF APOAPSIS > 100KM {\n CIRCULARISE 100KM;\n} ELSE {\n PRINT wait;\n}\n*WALK IN THE PARK*;";
        var errors = new List<string>();
        var commands = CommandParser.Parse(valid, errors);
        if (errors.Count != 0 || commands.Count != 15)
            Fail("valid command set", errors, commands.Count);

        errors.Clear();
        CommandParser.Parse("STAGE extra;\nSET READY TRUE;\nSCIENCE UNKNOWN;", errors);
        if (errors.Count != 3)
            Fail("malformed-command diagnostics", errors, errors.Count);

        errors.Clear();
        string longMission = "*WALK IN THE PARK*;\nGRAVTURN KERBIN;\nCIRCULARISE 100KM;\nTRANSFER TO MUN;\nLAND ON THE MUN;";
        CommandParser.Parse(longMission, errors);
        if (errors.Count != 0)
            Fail("long-mission syntax", errors, errors.Count);

        errors.Clear();
        string commandInventory = "using autostage;\nusing clearskies;\nusing autowarp;\nusing maxscience;\nusing safeabort;\nusing smartrcs;\nusing autoland;\nusing rendezvous;\nusing docking;\nusing recovery;\nLAUNCH TO DOCK TO Station Alpha;\nSTAGE;\nHOLD PG 1;\nHOLD RG 1;\nHOLD NML 1;\nHOLD ANML 1;\nHOLD RI 1;\nHOLD RO 1;\nCIRC;\nCIRCULARISE 100KM;\nTHROTTLE 65;\nSAS PG;\nRCS ON;\nRCS OFF;\nRCS TOGGLE;\nABORT;\nPRINT ready;\nGRAVTURN KERBIN;\nGRAVTURN POLAR KERBIN;\nPOLAR ORBIT;\nPOLAR ORBIT AROUND MUN;\nHOHMANN TO MUN;\nTRANSFER TO CB DUNA;\nEXECUTE NODE;\nALIGN PLANE TO TARGET;\nSCIENCE AUTO;\nSCIENCE COLLECT;\nSCIENCE TRANSMIT;\nDO SCIENCE;\nDEPLOY SOLAR PANELS;\nDISTANCE 50;\nRENDEZVOUS WITH TARGET;\nDOCK TO TARGET;\nAUTODOCK;\nAPOLLO DOCK;\nAPOLLO DOCK TO TARGET;\nTRANSPOSE DOCK;\nLAND ON THE MUN;\nRECOVER;\nIF ORBIT = SAFE {\n DEPLOY SOLAR PANELS;\n} ELSE {\n PRINT no;\n}\nWHEN ALTITUDE > 100KM {\n PRINT high;\n}\nSET READY = TRUE;\n*WALK IN THE PARK*;";
        CommandParser.Parse(commandInventory, errors);
        if (errors.Count != 0)
            Fail("command inventory", errors, errors.Count);

        errors.Clear();
        CommandParser.Parse("IF ORBIT = UNKNOWN {\n DEPLOY SOLAR PANELS;\n}", errors);
        bool orbitDiagnosticFound = false;
        for (int i = 0; i < errors.Count; i++)
        {
            if (errors[i].IndexOf("ORBIT state must be SAFE or UNSAFE", StringComparison.Ordinal) >= 0)
                orbitDiagnosticFound = true;
        }
        if (!orbitDiagnosticFound)
            Fail("orbit safety diagnostics", errors, errors.Count);

        errors.Clear();
        CommandParser.Parse("IF ORBIT = SAFE { DEPLOY SOLAR PANELS; };", errors);
        if (errors.Count != 0)
            Fail("inline safety block", errors, errors.Count);

        Console.WriteLine("KSP-sOS parser smoke test passed.");
    }

    private static void Fail(string testName, List<string> errors, int count)
    {
        throw new Exception(testName + " failed; count=" + count + "; " + string.Join(" | ", errors.ToArray()));
    }
}
