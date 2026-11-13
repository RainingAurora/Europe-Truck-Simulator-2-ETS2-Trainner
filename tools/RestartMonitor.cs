using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using Ets2Trainer;

internal static class RestartMonitor
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        int baseline = Int32.Parse(args[0]);
        int target = args.Length > 1 ? Int32.Parse(args[1]) : 3;
        int timeout = args.Length > 2 ? Int32.Parse(args[2]) : 900;
        System.IO.StreamWriter output=null;
        if(args.Length>3)
        {
            output=new System.IO.StreamWriter(args[3],false,new System.Text.UTF8Encoding(false));
            output.AutoFlush=true;Console.SetOut(output);
        }
        var seen = new HashSet<int>();
        var clock = Stopwatch.StartNew();
        ProcessMemory memory = null;
        string previous = null;
        string stableKey=null;
        int stableTicks=0;
        try
        {
            while (clock.Elapsed.TotalSeconds < timeout)
            {
                try
                {
                    if (memory != null && !memory.IsAlive) { memory.Dispose(); memory = null; }
                    if (memory == null) memory = ProcessMemory.Attach();
                    var s = GameReader.Read(memory);
                    var physics=StabilityReader.Read(memory,s);
                    var cores=new List<string>();
                    foreach(var body in physics.Bodies)foreach(var part in body.Parts)cores.Add(part.Core.ToString("X"));
                    string identity=memory.ProcessId+":"+s.FuelAddress+":"+s.FatigueAddress+":"+String.Join(",",cores);
                    bool resolved = s.CanFuel && s.CanFatigue && physics.Ready;
                    stableTicks=resolved?(stableKey==identity?stableTicks+1:1):0;stableKey=identity;
                    string key = memory.ProcessId + ":" + s.State + ":" + resolved;
                    bool added = memory.ProcessId != baseline && stableTicks>=6 && seen.Add(memory.ProcessId);
                    if (key != previous || added)
                    {
                        Console.WriteLine(new JavaScriptSerializer().Serialize(new {
                            time = DateTimeOffset.Now.ToString("o"), pid = memory.ProcessId,
                            module = memory.BaseAddress.ToString("X"), state = s.State,
                            fuel = s.Fuel, fatigue = s.Fatigue, fuelAddress = s.FuelAddress.ToString("X"),
                            fatigueAddress = s.FatigueAddress.ToString("X"), restartsValidated = seen.Count,
                            fuelIssue = s.FuelIssue, fatigueIssue = s.FatigueIssue,
                            physicsReady=physics.Ready,physicsIssue=physics.Issue,rigidBodies=cores,
                            truckRoll=physics.TruckRoll,trailerRoll=physics.TrailerRoll
                        }));
                        previous = key;
                    }
                    if (seen.Count >= target) return 0;
                }
                catch (Exception ex)
                {
                    stableTicks = 0;
                    stableKey = null;
                    if (previous != ex.Message) { Console.WriteLine(new JavaScriptSerializer().Serialize(new { time = DateTimeOffset.Now.ToString("o"), waiting = ex.Message, restartsValidated = seen.Count })); previous = ex.Message; }
                    if (memory != null) { memory.Dispose(); memory = null; }
                }
                Thread.Sleep(500);
            }
            Console.WriteLine(new JavaScriptSerializer().Serialize(new { time = DateTimeOffset.Now.ToString("o"), timedOut = true, restartsValidated = seen.Count }));
            Console.Error.WriteLine("Restart validation timed out after " + seen.Count + " successful restarts.");
            return 2;
        }
        finally { if(memory != null) memory.Dispose(); if(output!=null)output.Dispose(); }
    }
}
